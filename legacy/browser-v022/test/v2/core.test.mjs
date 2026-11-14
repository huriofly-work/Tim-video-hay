import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import vm from "node:vm";
import { videoType, normalizeVideo, shortTitle } from "../../src/domain.mjs";
import { channelLink } from "../../src/channels.mjs";
import { resolveDouyin } from "../../src/source-douyin.mjs";
import { Store } from "../../src/store.mjs";
import { Workflow } from "../../src/workflow.mjs";
import { createApplication } from "../../src/server-v2.mjs";
import { saveVideo, downloadVideo } from "../../src/media.mjs";
const id = "7663523864474930442",
  channelId = "UC" + "a".repeat(22),
  owner = { browserId: crypto.randomUUID(), sessionId: crypto.randomUUID() };
const video = {
  id,
  duration: 240,
  media: [
    { url: "https://www.douyinvod.com/video.mp4", width: 1920, height: 1080 },
  ],
};
async function fixture() {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-v2-"));
  const store = new Store(dir);
  Object.assign(store.state.settings, {
    channelUrl: "https://www.youtube.com/channel/" + channelId,
    outputDir: dir,
  });
  store.save();
  let downloads = 0;
  const app = new Workflow(store, {
    resolveDouyin: async () => ({
      platform: "douyin",
      id,
      url: "https://www.douyin.com/video/" + id,
    }),
    checkOutput: async (p) => p,
    saveVideo: async () => {
      downloads++;
      return { file: path.join(dir, "Video.mp4"), ok: true };
    },
  });
  app.register(owner, "0.2.1");
  return {
    dir,
    store,
    app,
    get downloads() {
      return downloads;
    },
    async close() {
      await fs.rm(dir, { recursive: true, force: true });
    },
  };
}
async function completeBrowser(app, owner, payload) {
  const job = app.next(owner);
  assert.ok(job.id);
  await app.receive(owner, { jobId: job.id, lease: job.lease, ...payload });
  return job;
}
test("four-minute boundary and Windows names are preserved without truncation", () => {
  assert.equal(videoType(239.999), "Shorts");
  assert.equal(videoType(240), "Shorts");
  assert.equal(videoType(240.001), "Long");
  assert.equal(videoType(null), null);
  assert.equal(shortTitle("CON"), "Video CON");
  assert.equal(shortTitle("Hố đen !!!"), "Hố đen");
});
test("channel configuration accepts user links and rejects unrelated/video/credential URLs", () => {
  assert.equal(
    channelLink("https://www.youtube.com/channel/" + channelId).id,
    channelId,
  );
  assert.equal(
    channelLink("https://youtube.com/@NullSeek").url,
    "https://www.youtube.com/@NullSeek",
  );
  for (const value of [
    "https://youtube.com/watch?v=x",
    "https://youtube.com.evil/@name",
    "https://user:password@youtube.com/@name",
    "http://youtube.com/@name",
    "https://studio.youtube.com/channel/wrong",
  ])
    assert.throws(() => channelLink(value));
});
test("Douyin redirect validates each hop and never follows a foreign host", async () => {
  let calls = 0;
  const result = await resolveDouyin(
    "Chia sẻ https://v.douyin.com/short/",
    async () => {
      calls++;
      return {
        headers: new Headers({
          location: "https://www.douyin.com/video/" + id,
        }),
        body: { cancel: async () => {} },
      };
    },
  );
  assert.equal(result.id, id);
  assert.equal(calls, 1);
  await assert.rejects(
    resolveDouyin("https://v.douyin.com/x/", async () => ({
      headers: new Headers({ location: "https://evil.com/video/" + id }),
      body: { cancel: async () => {} },
    })),
    /Douyin/,
  );
  await assert.rejects(
    resolveDouyin("https://youtube.com/watch?v=x"),
    /Douyin/,
  );
});
test("migration copies exact legacy state before replacing schema and retains output/Sheets settings", async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-v2-migrate-"));
  try {
    const legacy = {
      version: 1,
      settings: {
        sheetId: "sheet",
        sheetTab: "Tab",
        outputDir: dir,
        model: "old",
      },
      exports: { [id]: { sheet: { row: 10 } } },
      rejected: { a: true },
    };
    await fs.writeFile(path.join(dir, "state.json"), JSON.stringify(legacy));
    const store = new Store(dir);
    assert.equal(store.state.version, 2);
    assert.equal(store.state.settings.sheetId, "sheet");
    assert.equal(store.state.settings.outputDir, dir);
    assert.equal(store.state.settings.model, undefined);
    assert.deepEqual(
      JSON.parse(await fs.readFile(store.state.legacyBackup, "utf8")),
      legacy,
    );
    assert.equal(new Store(dir).state.legacyBackup, store.state.legacyBackup);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
test("a different browser profile cannot claim, inject, stop or resume another job", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Hố đen", niche: "Vũ trụ" }, owner);
    const other = {
      browserId: crypto.randomUUID(),
      sessionId: crypto.randomUUID(),
    };
    f.app.register(other, "0.2.1");
    assert.deepEqual(f.app.next(other), {});
    const job = f.app.next(owner);
    assert.equal(
      await f.app.receive(other, {
        jobId: job.id,
        lease: job.lease,
        sheetReady: true,
        channelId,
      }),
      false,
    );
    assert.throws(() => f.app.stop(other));
    f.app.stop(owner);
    await assert.rejects(f.app.resume(other), /profile/);
    assert.equal(
      await f.app.receive(owner, {
        jobId: job.id,
        lease: job.lease,
        sheetReady: true,
        channelId,
      }),
      false,
    );
  } finally {
    await f.close();
  }
});
test("outdated extension cannot claim or finish jobs under the fixed audience contract", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Tên", niche: "Ngách" }, owner);
    f.app.register(owner, "0.2.0");
    assert.deepEqual(f.app.next(owner), {});
    f.app.register(owner, "0.2.1");
    const job = f.app.next(owner);
    f.app.register(owner, "0.2.0");
    assert.equal(
      await f.app.receive(owner, {
        jobId: job.id,
        lease: job.lease,
        sheetReady: true,
        channelId,
      }),
      false,
    );
    assert.equal(f.app.current.phase, "preflight");
  } finally {
    await f.close();
  }
});
test("pipeline checks login/channel before Sheets, saves only video, and requires verified unlisted outcome", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Hố đen", niche: "Vũ trụ" }, owner);
    assert.equal(f.app.current.phase, "preflight");
    await completeBrowser(f.app, owner, { sheetReady: true, channelId });
    assert.equal(f.app.current.phase, "detail");
    await completeBrowser(f.app, owner, { video });
    assert.equal(f.app.current.type, "Shorts");
    const sheet = f.app.next(owner);
    await f.app.receive(owner, {
      jobId: sheet.id,
      lease: sheet.lease,
      sheet: { ok: true, row: 20 },
    });
    await f.app.work;
    assert.equal(f.downloads, 1);
    const upload = f.app.next(owner);
    assert.equal(upload.kind, "youtube");
    assert.equal(upload.channelId, channelId);
    assert.equal(upload.title, undefined);
    await assert.rejects(
      f.app.receive(owner, {
        jobId: upload.id,
        lease: upload.lease,
        youtube: { id: "abcdefghijk", channelId, visibility: "private" },
      }),
      /không công khai/,
    );
  } finally {
    await f.close();
  }
});
test("manual-login attention resumes without redoing the saved Sheets or downloaded file", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Hố đen", niche: "Vũ trụ" }, owner);
    await completeBrowser(f.app, owner, { sheetReady: true, channelId });
    await completeBrowser(f.app, owner, { video });
    await completeBrowser(f.app, owner, { sheet: { ok: true, row: 20 } });
    await f.app.work;
    const upload = f.app.next(owner);
    await f.app.receive(owner, {
      jobId: upload.id,
      lease: upload.lease,
      attention: true,
      message: "Đăng nhập",
    });
    await fs.writeFile(f.app.current.download.file, "video");
    await f.app.resume(owner);
    assert.equal(f.app.current.phase, "youtube");
    assert.equal(f.downloads, 1);
    assert.equal(f.app.current.sheet.row, 20);
  } finally {
    await f.close();
  }
});
test("lost upload result blocks automatic resend, retains early YouTube ID and verifies existing video", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Hố đen", niche: "Vũ trụ" }, owner);
    await completeBrowser(f.app, owner, { sheetReady: true, channelId });
    await completeBrowser(f.app, owner, { video });
    await completeBrowser(f.app, owner, { sheet: { ok: true, row: 20 } });
    await f.app.work;
    const job = f.app.next(owner);
    f.app.progress(owner, {
      jobId: job.id,
      lease: job.lease,
      uploadStarted: true,
    });
    f.app.stop(owner);
    await assert.rejects(f.app.resume(owner), /chưa rõ/);
    f.app.reconcile(owner, {
      url: "https://www.youtube.com/watch?v=abcdefghijk",
    });
    await f.app.resume(owner);
    const next = f.app.next(owner);
    assert.equal(next.kind, "youtube-confirm");
    assert.equal(next.youtubeId, "abcdefghijk");
    await f.app.receive(owner, {
      jobId: next.id,
      lease: next.lease,
      youtube: {
        id: "abcdefghijk",
        channelId,
        visibility: "unlisted",
        audience: "not-kids",
      },
    });
    assert.equal(f.app.current.status, "complete");
    assert.equal(f.downloads, 1);
    await assert.rejects(
      f.app.start({ url: "x", title: "Khác", niche: "Vũ trụ" }, owner),
      /đã có/,
    );
  } finally {
    await f.close();
  }
});
test("restart during upload records uncertain outcome and never requeues blindly", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Hố đen", niche: "Vũ trụ" }, owner);
    f.app.current.phase = "youtube";
    f.store.save();
    const resumed = new Workflow(new Store(f.dir));
    assert.equal(resumed.current.status, "attention");
    assert.equal(resumed.current.outcomeUnknown, true);
    assert.deepEqual(resumed.next(owner), {});
  } finally {
    await f.close();
  }
});
test("expired worker lease pauses the same session, rejects late writes and keeps the sheet checkpoint", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Tên", niche: "Ngách" }, owner);
    await completeBrowser(f.app, owner, { sheetReady: true, channelId });
    await completeBrowser(f.app, owner, { video });
    const job = f.app.next(owner);
    f.app.progress(owner, {
      jobId: job.id,
      lease: job.lease,
      sheetRow: 30,
      sheetColumn: 2,
    });
    f.app.current.browserJob.touchedAt = Date.now() - 46000;
    f.app.public(owner);
    assert.equal(f.app.current.status, "attention");
    assert.equal(
      f.app.progress(owner, { jobId: job.id, lease: job.lease, sheetRow: 99 }),
      false,
    );
    f.app.register(owner, "0.2.1");
    await f.app.resume(owner);
    const resumed = f.app.next(owner);
    assert.equal(resumed.pendingRow, 30);
    assert.notEqual(resumed.lease, job.lease);
  } finally {
    await f.close();
  }
});
test("expired upload lease records ambiguity and blocks a second upload without reconciliation", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Tên", niche: "Ngách" }, owner);
    f.app.current.phase = "youtube";
    const job = f.app.next(owner);
    f.app.current.browserJob.touchedAt = Date.now() - 46000;
    f.app.register(owner, "0.2.1");
    assert.equal(f.app.current.outcomeUnknown, true);
    assert.equal(f.app.current.status, "attention");
    await assert.rejects(f.app.resume(owner), /chưa rõ/);
    assert.equal(
      await f.app.receive(owner, {
        jobId: job.id,
        lease: job.lease,
        youtube: { id: "abcdefghijk", channelId, visibility: "unlisted" },
      }),
      false,
    );
  } finally {
    await f.close();
  }
});
test("sheet checkpoint keeps pending row when interrupted; stale lease cannot progress", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Tên", niche: "Ngách" }, owner);
    await completeBrowser(f.app, owner, { sheetReady: true, channelId });
    await completeBrowser(f.app, owner, { video });
    const job = f.app.next(owner);
    assert.equal(
      f.app.progress(owner, {
        jobId: job.id,
        lease: job.lease,
        sheetRow: 30,
        sheetColumn: 2,
      }),
      true,
    );
    f.app.stop(owner);
    assert.equal(
      f.app.progress(owner, { jobId: job.id, lease: job.lease, sheetRow: 99 }),
      false,
    );
    await f.app.resume(owner);
    assert.equal(f.app.next(owner).pendingRow, 30);
  } finally {
    await f.close();
  }
});
test("identical source cannot be posted twice through different URLs of the same channel", async () => {
  const f = await fixture();
  try {
    f.store.state.jobs.old = {
      id: "old",
      source: { id },
      settings: { channelUrl: "https://www.youtube.com/@name" },
      channel: { id: channelId },
      youtube: { id: "abcdefghijk" },
      owner,
      status: "complete",
    };
    await f.app.start({ url: "x", title: "Tên", niche: "Ngách" }, owner);
    await completeBrowser(f.app, owner, { sheetReady: true, channelId });
    assert.equal(f.app.current.status, "attention");
    assert.equal(f.app.current.phase, "preflight");
  } finally {
    await f.close();
  }
});
test("different-profile settings do not alter the snapshot of an active upload", async () => {
  const f = await fixture();
  try {
    await f.app.start({ url: "x", title: "Tên", niche: "Ngách" }, owner);
    f.store.state.settings.channelUrl = "https://youtube.com/@other";
    assert.equal(
      f.app.current.settings.channelUrl,
      "https://www.youtube.com/channel/" + channelId,
    );
    await assert.rejects(
      f.app.start({ url: "x", title: "Tên", niche: "Ngách" }, owner),
      /Đang có/,
    );
  } finally {
    await f.close();
  }
});
test("HTTP session, origin and profile boundaries deny unauthorized operations", async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-v2-http-"));
  const app = createApplication({
    dir,
    services: { checkOutput: async (p) => p },
  });
  await new Promise((r) => app.server.listen(0, "127.0.0.1", r));
  try {
    const base = "http://127.0.0.1:" + app.server.address().port;
    assert.equal((await fetch(base + "/api/state")).status, 401);
    const home = await fetch(base);
    const cookie = home.headers.get("set-cookie").split(";")[0];
    assert.equal(
      (
        await fetch(base + "/api/settings", {
          method: "POST",
          headers: { cookie, origin: "https://evil.com" },
          body: "{}",
        })
      ).status,
      403,
    );
    assert.equal((await fetch(base + "/bridge/job")).status, 401);
    const headers = {
      authorization: "Bearer " + app.store.state.bridgeToken,
      "content-type": "application/json",
      "x-browser-id": owner.browserId,
      "x-app-session": owner.sessionId,
      "x-video-extension-version": "0.2.1",
    };
    assert.equal(
      (
        await fetch(base + "/bridge/register", {
          method: "POST",
          headers,
          body: JSON.stringify(owner),
        })
      ).status,
      200,
    );
    assert.equal(
      (
        await fetch(base + "/api/settings?" + new URLSearchParams(owner), {
          method: "POST",
          headers: { cookie, origin: base, "content-type": "application/json" },
          body: JSON.stringify({
            channelUrl: "https://youtube.com/@name",
            outputDir: dir,
            audience: "kids",
          }),
        })
      ).status,
      200,
    );
    assert.equal(
      app.store.state.settings.channelUrl,
      "https://www.youtube.com/@name",
    );
    assert.equal(app.store.state.settings.audience, "not-kids");
    assert.equal((await fetch(base + "/api/start")).status, 401);
    assert.equal(
      (
        await fetch(base + "/api/google/begin", {
          method: "POST",
          headers: { cookie, origin: base },
          body: "{}",
        })
      ).status,
      409,
    );
  } finally {
    app.server.closeAllConnections();
    await new Promise((r) => app.server.close(r));
    await fs.rm(dir, { recursive: true, force: true });
  }
});
test("browser sheet policy supports nonadjacent columns, quoted newlines and duplicate reuse", async () => {
  const context = { URL };
  vm.runInNewContext(
    await fs.readFile(
      new URL("../../extension/sheet-policy.js", import.meta.url),
      "utf8",
    ),
    context,
  );
  const policy = context.sheetPolicy;
  const rows = policy.parseSheetTsv(
    "Owner\tLink video mẫu\tOther\tTên video\tNgách\tLoại video\nAlice\t\tKeep\t\t\t\nBob\thttps://www.douyin.com/video/" +
      id +
      "\tKeep\tTên\tNgách\tVideo ngắn",
  );
  assert.deepEqual(Array.from(policy.sheetColumns(rows).columns), [1, 3, 4, 5]);
  assert.equal(policy.sheetPlan(rows, id).row, 3);
  assert.equal(policy.sheetPlan(rows, id).duplicate, true);
  assert.equal(policy.sheetPlan(rows, "other").row, 2);
  assert.equal(policy.parseSheetTsv('"a\nb"\tx')[0][0], "a\nb");
  assert.equal(policy.sheetLetter(27), "AB");
  assert.equal(
    policy.sheetVideoId("https://evil.com/douyin.com/video/" + id),
    null,
  );
  assert.throws(() =>
    policy.sheetColumns([
      ["Link video mẫu", "Link video mẫu", "Tên video", "Ngách", "Loại video"],
    ]),
  );
});
const info = (width = 1920, height = 1080) => ({
  streams: [
    { codec_type: "video", width, height, codec_name: "hevc" },
    { codec_type: "audio" },
  ],
  format: { duration: "240" },
});
test("download keeps highest verified quality and preserves the exact file contents", async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-v2-media-"));
  try {
    const attempted = [];
    const result = await downloadVideo(
      {
        ...video,
        media: [
          { url: "1080", width: 1920, height: 1080, bitrate: 9000000 },
          { url: "2k", width: 2560, height: 1440, bitrate: 2000000 },
        ],
      },
      path.join(dir, "v.mp4"),
      null,
      {
        download: async (url, file) => {
          attempted.push(url);
          await fs.writeFile(file, "original bytes");
        },
        probe: async () => info(2560, 1440),
      },
    );
    assert.deepEqual(attempted, ["2k"]);
    assert.equal(result.height, 1440);
    assert.equal(
      await fs.readFile(path.join(dir, "v.mp4"), "utf8"),
      "original bytes",
    );
    assert.equal(result.originalVerified, false);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
test("video checkpoint revalidates files, retries missing files and never creates covers/subtitles", async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-v2-checkpoint-"));
  let calls = 0;
  try {
    const options = {
      stateDir: dir,
      probe: async () => info(),
      downloadVideo: async (_, file) => {
        calls++;
        await fs.writeFile(file, "bytes");
        return { width: 1920, height: 1080 };
      },
    };
    const a = await saveVideo(video, "Tên", dir, null, options);
    const b = await saveVideo(video, "Tên", dir, null, options);
    assert.equal(a.file, b.file);
    assert.equal(calls, 1);
    await fs.unlink(a.file);
    await saveVideo(video, "Tên", dir, null, options);
    assert.equal(calls, 2);
    assert.deepEqual(await fs.readdir(a.dir), ["Tên.mp4"]);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
test("downloading the same source under a different name preserves the earlier file and honors the requested filename", async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-v2-names-"));
  try {
    const options = {
      stateDir: dir,
      probe: async () => info(),
      downloadVideo: async (_, file) => {
        await fs.writeFile(file, "original bytes");
        return { width: 1920, height: 1080 };
      },
    };
    const first = await saveVideo(video, "Tên đầu", dir, null, options);
    const second = await saveVideo(video, "Tên mới", dir, null, options);
    assert.equal(path.basename(first.file), "Tên đầu.mp4");
    assert.equal(path.basename(second.file), "Tên mới.mp4");
    assert.notEqual(first.file, second.file);
    assert.equal(await fs.readFile(first.file, "utf8"), "original bytes");
    assert.equal(await fs.readFile(second.file, "utf8"), "original bytes");
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
test("structured variants outrank conflicting default dimensions", () => {
  const v = normalizeVideo({
    aweme_id: id,
    video: {
      width: 2560,
      height: 1440,
      play_addr: { url_list: ["same"] },
      bit_rate: [
        {
          bit_rate: 4000000,
          play_addr: { width: 1920, height: 1080, url_list: ["same"] },
        },
        {
          bit_rate: 2000000,
          is_h265: 1,
          play_addr: { width: 2560, height: 1440, url_list: ["2k"] },
        },
      ],
    },
  });
  assert.equal(v.media.length, 2);
  assert.equal(v.media[0].height, 1440);
  assert.equal(v.media[1].height, 1080);
});
