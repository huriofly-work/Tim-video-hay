import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import { createApplication } from "../../src/server-v2.mjs";
import { Workflow } from "../../src/workflow.mjs";
const id = "7663523864474930442",
  channelId = "UC" + "a".repeat(22);
const owner = {
  browserId: crypto.randomUUID(),
  sessionId: crypto.randomUUID(),
};
const video = {
  id,
  duration: 240,
  media: [
    { url: "https://www.douyinvod.com/video.mp4", width: 1920, height: 1080 },
  ],
};
async function fixture(saveVideo) {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-standalone-"));
  let saves = 0;
  const services = {
    resolveDouyin: async () => ({
      platform: "douyin",
      id,
      url: "https://www.douyin.com/video/" + id,
    }),
    checkOutput: async (p) => {
      assert.equal(p, dir);
      return p;
    },
    saveVideo:
      saveVideo ||
      (async () => {
        saves++;
        return { file: path.join(dir, "Tên.mp4"), width: 1920, height: 1080 };
      }),
  };
  const app = createApplication({ dir, services });
  app.workflow.register(owner, "0.2.1");
  app.downloads.register(owner, "0.2.1");
  return {
    ...app,
    dir,
    services,
    get saves() {
      return saves;
    },
    async close() {
      if (app.server.listening) {
        app.server.closeAllConnections();
        await new Promise((r) => app.server.close(r));
      }
      await fs.rm(dir, { recursive: true, force: true });
    },
  };
}
const start = (f) =>
  f.downloads.start({ url: "x", title: "Tên", outputDir: f.dir }, owner);
async function result(runner, payload) {
  const job = runner.next(owner);
  assert.ok(job.id);
  await runner.receive(owner, { jobId: job.id, lease: job.lease, ...payload });
  return job;
}
async function pipelineToDownload(f) {
  Object.assign(f.store.state.settings, {
    channelUrl: "https://www.youtube.com/channel/" + channelId,
    outputDir: f.dir,
  });
  await f.workflow.start({ url: "x", title: "Tên", niche: "Vũ trụ" }, owner);
  await result(f.workflow, { sheetReady: true, channelId });
  await result(f.workflow, { video });
  await result(f.workflow, { sheet: { ok: true, row: 10 } });
}
test("download-only needs no channel, niche, Google setup or pipeline output folder and completes after saving video", async () => {
  const f = await fixture();
  try {
    f.store.state.settings.channelUrl = "";
    f.store.state.settings.outputDir = "";
    f.store.state.settings.sheetId = "not configured";
    await start(f);
    assert.equal(f.downloads.current.phase, "detail");
    assert.deepEqual(f.downloads.current.settings, { outputDir: f.dir });
    await result(f.downloads, { video });
    await f.downloads.work;
    assert.equal(f.saves, 1);
    assert.equal(f.downloads.current.status, "complete");
    assert.deepEqual(f.downloads.next(owner), {});
    assert.deepEqual(f.downloads.current.sheet, {});
    assert.deepEqual(f.downloads.current.youtube, {});
    assert.equal(f.workflow.current, null);
    assert.equal(f.workflow.public(owner).history.length, 0);
    assert.equal(f.downloads.public(owner).history.length, 1);
  } finally {
    await f.close();
  }
});
test("unfinished upload and its reconciliation remain unchanged while a separate download runs", async () => {
  const f = await fixture();
  try {
    Object.assign(f.store.state.settings, {
      channelUrl: "https://www.youtube.com/channel/" + channelId,
      outputDir: f.dir,
    });
    await f.workflow.start({ url: "x", title: "Tên", niche: "Vũ trụ" }, owner);
    Object.assign(f.workflow.current, {
      status: "error",
      phase: "youtube",
      outcomeUnknown: true,
    });
    const previous = structuredClone(f.workflow.current);
    await start(f);
    await result(f.downloads, { video });
    await f.downloads.work;
    assert.deepEqual(f.workflow.current, previous);
    assert.equal(f.downloads.current.status, "complete");
  } finally {
    await f.close();
  }
});
test("stopping download-only invalidates its lease without stopping the pipeline; resume keeps its own task", async () => {
  const f = await fixture();
  try {
    Object.assign(f.store.state.settings, {
      channelUrl: "https://www.youtube.com/channel/" + channelId,
      outputDir: f.dir,
    });
    await f.workflow.start({ url: "x", title: "Tên", niche: "Vũ trụ" }, owner);
    await start(f);
    const primary = f.workflow.current.id,
      downloadId = f.downloads.current.id,
      job = f.downloads.next(owner);
    f.downloads.stop(owner);
    assert.equal(
      await f.downloads.receive(owner, {
        jobId: job.id,
        lease: job.lease,
        video,
      }),
      false,
    );
    assert.equal(f.saves, 0);
    assert.equal(f.workflow.current.id, primary);
    assert.equal(f.workflow.current.status, "running");
    await f.downloads.resume(owner);
    assert.equal(f.downloads.current.id, downloadId);
    await result(f.downloads, { video });
    await f.downloads.work;
    assert.equal(f.downloads.current.status, "complete");
  } finally {
    await f.close();
  }
});
test("restart restores a standalone download from its own pointer and resumes no Sheets/YouTube step", async () => {
  const f = await fixture();
  try {
    await start(f);
    Object.assign(f.downloads.current, { phase: "download", video });
    f.store.save();
    const resumed = new Workflow(f.store, f.services, { downloadOnly: true });
    resumed.register(owner, "0.2.1");
    assert.equal(resumed.current.status, "attention");
    await resumed.resume(owner);
    await resumed.work;
    assert.equal(resumed.current.status, "complete");
    assert.equal(f.workflow.current, null);
    assert.deepEqual(resumed.current.youtube, {});
  } finally {
    await f.close();
  }
});
test("HTTP standalone routes use their own state and bridge jobs without channel settings", async () => {
  const f = await fixture();
  await new Promise((r) => f.server.listen(0, "127.0.0.1", r));
  try {
    const base = "http://127.0.0.1:" + f.server.address().port;
    const cookie = (await fetch(base)).headers.get("set-cookie").split(";")[0];
    const bridge = {
      authorization: "Bearer " + f.store.state.bridgeToken,
      "content-type": "application/json",
      "x-browser-id": owner.browserId,
      "x-app-session": owner.sessionId,
      "x-video-extension-version": "0.2.1",
    };
    await fetch(base + "/bridge/register", {
      method: "POST",
      headers: bridge,
      body: JSON.stringify(owner),
    });
    const endpoint = base + "/api/download/start?" + new URLSearchParams(owner),
      headers = { cookie, origin: base, "content-type": "application/json" };
    assert.equal(
      (
        await fetch(endpoint, {
          method: "POST",
          headers: { ...headers, origin: "https://evil.com" },
          body: "{}",
        })
      ).status,
      403,
    );
    assert.equal(
      (
        await fetch(endpoint, {
          method: "POST",
          headers,
          body: JSON.stringify({ url: "x", title: "Tên", outputDir: f.dir }),
        })
      ).status,
      200,
    );
    const job = await (
      await fetch(base + "/bridge/job", { headers: bridge })
    ).json();
    assert.equal(job.kind, "detail");
    assert.equal(
      (
        await (
          await fetch(base + "/bridge/active", {
            method: "POST",
            headers: bridge,
            body: JSON.stringify({ jobId: job.id, lease: job.lease }),
          })
        ).json()
      ).active,
      true,
    );
    const response = await fetch(base + "/bridge/result", {
      method: "POST",
      headers: bridge,
      body: JSON.stringify({ jobId: job.id, lease: job.lease, video }),
    });
    assert.equal((await response.json()).ok, true);
    await f.downloads.work;
    const state = await (
      await fetch(base + "/api/state?" + new URLSearchParams(owner), {
        headers: { cookie },
      })
    ).json();
    assert.equal(state.current, null);
    assert.equal(state.history.length, 0);
    assert.equal(state.downloadOnly.current.status, "complete");
    assert.equal(state.downloadOnly.history.length, 1);
    assert.deepEqual(
      await (await fetch(base + "/bridge/job", { headers: bridge })).json(),
      {},
    );
  } finally {
    await f.close();
  }
});
test("pipeline and standalone saves serialize shared checkpoints even when both target the same source and folder", async () => {
  let release,
    calls = 0,
    active = 0,
    maxActive = 0;
  const gate = new Promise((r) => {
    release = r;
  });
  const f = await fixture(async () => {
    const call = ++calls;
    active++;
    maxActive = Math.max(maxActive, active);
    if (call === 1) await gate;
    active--;
    return { file: "F:\\Videos\\Tên.mp4" };
  });
  try {
    await pipelineToDownload(f);
    await start(f);
    await result(f.downloads, { video });
    await new Promise(setImmediate);
    assert.equal(calls, 1);
    release();
    await Promise.all([f.workflow.work, f.downloads.work]);
    assert.equal(calls, 2);
    assert.equal(maxActive, 1);
    assert.equal(f.workflow.current.phase, "youtube");
    assert.equal(f.downloads.current.status, "complete");
  } finally {
    release();
    await f.close();
  }
});
test("a stopped standalone task waiting behind another save never begins file I/O", async () => {
  let release,
    calls = 0;
  const gate = new Promise((r) => {
    release = r;
  });
  const f = await fixture(async () => {
    calls++;
    await gate;
    return { file: "F:\\Videos\\Tên.mp4" };
  });
  try {
    await pipelineToDownload(f);
    await start(f);
    await result(f.downloads, { video });
    f.downloads.stop(owner);
    release();
    await Promise.all([f.workflow.work, f.downloads.work]);
    assert.equal(calls, 1);
    assert.equal(f.downloads.current.status, "stopped");
    assert.equal(f.workflow.current.phase, "youtube");
  } finally {
    release();
    await f.close();
  }
});
