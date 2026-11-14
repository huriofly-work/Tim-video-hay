import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import { sheetLink, sheetTabName } from "../../src/sheets.mjs";
import { Store } from "../../src/store.mjs";
import { createApplication } from "../../src/server-v2.mjs";
const id = "1" + "B".repeat(43),
  url = "https://docs.google.com/spreadsheets/d/" + id + "/edit";
const owner = {
  browserId: crypto.randomUUID(),
  sessionId: crypto.randomUUID(),
};
test("Google Sheets share URLs normalize to the target spreadsheet, including account-specific editor paths", () => {
  for (const link of [
    url + "?usp=sharing#gid=123",
    url.replace("/d/", "/u/2/d/"),
    url.replace("/edit", ""),
  ])
    assert.deepEqual(sheetLink(link), { id, url });
});
test("Google Sheets settings reject foreign hosts, credentials, published pages and other document types", () => {
  for (const link of [
    "",
    id,
    url.replace("https:", "http:"),
    url.replace("docs.google.com", "docs.google.com.evil.com"),
    url.replace("docs.google.com", "user:password@docs.google.com"),
    url.replace("docs.google.com", "docs.google.com:444"),
    url.replace("/spreadsheets/", "/document/"),
    url.replace("/d/", "/d/e/") + "/pubhtml",
  ])
    assert.throws(() => sheetLink(link));
});
test("tab names preserve Vietnamese, normalize whitespace and reject blank/control-character names", () => {
  assert.equal(sheetTabName("  Bảng công việc mới  "), "Bảng công việc mới");
  for (const name of ["", "  ", "Tab\nkhác", "a".repeat(101)])
    assert.throws(() => sheetTabName(name));
});
async function fixture() {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-sheet-settings-"));
  const app = createApplication({
    dir,
    services: {
      checkOutput: async (p) => p,
      resolveDouyin: async () => ({
        platform: "douyin",
        id: "7663523864474930442",
        url: "https://www.douyin.com/video/7663523864474930442",
      }),
    },
  });
  await new Promise((r) => app.server.listen(0, "127.0.0.1", r));
  const base = "http://127.0.0.1:" + app.server.address().port;
  const cookie = (await fetch(base)).headers.get("set-cookie").split(";")[0];
  return {
    ...app,
    dir,
    base,
    cookie,
    post: (body) =>
      fetch(base + "/api/sheets", {
        method: "POST",
        headers: { cookie, origin: base, "content-type": "application/json" },
        body: JSON.stringify(body),
      }),
    async close() {
      app.server.closeAllConnections();
      await new Promise((r) => app.server.close(r));
      await fs.rm(dir, { recursive: true, force: true });
    },
  };
}
test("changing Sheets persists independently of channel, extension pairing and standalone download state", async () => {
  const f = await fixture();
  try {
    assert.equal(
      (
        await fetch(f.base + "/api/sheets", {
          method: "POST",
          headers: { origin: f.base },
          body: "{}",
        })
      ).status,
      401,
    );
    assert.equal(
      (
        await fetch(f.base + "/api/sheets", {
          method: "POST",
          headers: { cookie: f.cookie, origin: "https://evil.com" },
          body: "{}",
        })
      ).status,
      403,
    );
    f.downloads.register(owner, "0.2.1");
    await f.downloads.start(
      { url: "x", title: "Tên", outputDir: f.dir },
      owner,
    );
    const download = structuredClone(f.downloads.current);
    assert.equal(
      (await f.post({ sheetUrl: url, sheetTab: "Bảng mới" })).status,
      200,
    );
    assert.equal(f.store.state.settings.channelUrl, "");
    assert.deepEqual(f.downloads.current, download);
    const persisted = new Store(f.dir);
    assert.equal(persisted.state.settings.sheetId, id);
    assert.equal(persisted.state.settings.sheetTab, "Bảng mới");
  } finally {
    await f.close();
  }
});
test("invalid settings leave both spreadsheet ID and tab unchanged", async () => {
  const f = await fixture();
  try {
    const before = structuredClone(f.store.state.settings);
    for (const input of [
      { sheetUrl: "https://evil.com", sheetTab: "Bảng mới" },
      { sheetUrl: url, sheetTab: "" },
    ]) {
      assert.equal((await f.post(input)).status, 400);
      assert.deepEqual(f.store.state.settings, before);
    }
  } finally {
    await f.close();
  }
});
test("running jobs block destination changes; stopped jobs retain their original Sheet when resumed", async () => {
  const f = await fixture();
  try {
    Object.assign(f.store.state.settings, {
      channelUrl: "https://youtube.com/channel/UC" + "a".repeat(22),
      outputDir: f.dir,
    });
    f.workflow.register(owner, "0.2.1");
    await f.workflow.start({ url: "x", title: "Tên", niche: "Ngách" }, owner);
    const originalId = f.workflow.current.settings.sheetId,
      originalTab = f.workflow.current.settings.sheetTab;
    assert.equal(
      (await f.post({ sheetUrl: url, sheetTab: "Bảng mới" })).status,
      400,
    );
    assert.equal(f.store.state.settings.sheetId, originalId);
    f.workflow.stop(owner);
    assert.equal(
      (await f.post({ sheetUrl: url, sheetTab: "Bảng mới" })).status,
      200,
    );
    await f.workflow.resume(owner);
    const job = f.workflow.next(owner);
    assert.equal(job.sheetId, originalId);
    assert.equal(job.sheetTab, originalTab);
    assert.equal(f.store.state.settings.sheetId, id);
  } finally {
    await f.close();
  }
});
