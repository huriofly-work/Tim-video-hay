import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import vm from "node:vm";
const load = async (context, files) => {
  for (const name of files)
    vm.runInNewContext(
      await fs.readFile(
        new URL("../../extension/" + name, import.meta.url),
        "utf8",
      ),
      context,
    );
  return context;
};
test("browser driver never sends input after redirect to login, foreign origin or stopped job", async () => {
  let url = "https://accounts.google.com/signin",
    calls = 0,
    active = true;
  const context = await load(
    {
      URL,
      chrome: {
        tabs: { get: async () => ({ url }) },
        debugger: {
          sendCommand: async () => {
            calls++;
          },
        },
      },
    },
    ["browser-driver.js"],
  );
  const driver = new context.BrowserDriver(
    1,
    "https://docs.google.com",
    async () => active,
    async () => {},
  );
  await assert.rejects(driver.text("private"), /đăng nhập/);
  url = "https://evil.com";
  await assert.rejects(driver.key("Enter", "Enter"), /địa chỉ/);
  url = "https://docs.google.com/spreadsheets/x";
  active = false;
  await assert.rejects(driver.text("value"), /dừng/);
  assert.equal(calls, 0);
});
test("confirmation refuses a different video even when it stays on the Studio origin", async () => {
  let calls = 0;
  const context = await load(
    {
      URL,
      chrome: {
        tabs: {
          get: async () => ({
            url: "https://studio.youtube.com/video/wrongvideo1/edit",
          }),
        },
        debugger: {
          sendCommand: async () => {
            calls++;
          },
        },
      },
    },
    ["browser-driver.js"],
  );
  const driver = new context.BrowserDriver(
    1,
    "https://studio.youtube.com",
    async () => true,
    async () => {},
  );
  driver.expectedPath = "/video/abcdefghijk/edit";
  await assert.rejects(
    driver.page(() => true),
    /video khác/,
  );
  assert.equal(calls, 0);
});
async function sheetFixture(targetValues = ["", "", "", ""]) {
  const context = await load({ URL, crypto: { randomUUID: () => "nonce" } }, [
    "browser-driver.js",
    "sheet-policy.js",
    "sheets-browser.js",
  ]);
  const cells = new Map(
    ["B2", "D2", "F2", "H2"].map((key, i) => [key, targetValues[i]]),
  );
  const writes = [];
  let address = "A1";
  const rows =
    "Owner\tLink video mẫu\tOther\tTên video\tOther\tNgách\tOther\tLoại video\nKeep\t\tKeep\t\tKeep\t\tKeep\t\nOld\thttps://www.douyin.com/video/7663523864474930449\tKeep\tCũ\tKeep\tVũ trụ\tKeep\tVideo dài";
  context.openSheet = async () => {};
  context.sheetCopy = async () => rows;
  context.sheetSelect = async (_, value) => {
    address = value;
  };
  context.sheetCell = async (_, value) => {
    address = value;
    return cells.get(value) ?? "";
  };
  const driver = {
    page: async (fn) =>
      fn.name === "sheetNameBox"
        ? { value: "H3" }
        : fn.name === "sheetSaved"
          ? true
          : [],
    key: async () => {},
    text: async (value) => {
      writes.push({ address, value });
      cells.set(address, value);
    },
    wait: async (read, accept, message) => {
      const value = await read();
      if (!accept(value)) throw new Error(message);
      return value;
    },
  };
  const job = {
    sheetTab: "Bảng công việc",
    videoId: "7663523864474930442",
    url: "https://www.douyin.com/video/7663523864474930442",
    title: "Hố đen",
    niche: "Vũ trụ",
    type: "Shorts",
  };
  return { context, driver, job, cells, writes };
}
test("Sheets writes only four nonadjacent cells and adopts observed Vietnamese type labels", async () => {
  const f = await sheetFixture();
  const result = await f.context.writeSheet(f.driver, f.job, async () => {});
  assert.equal(result.row, 2);
  assert.deepEqual(
    f.writes.map((x) => x.address),
    ["B2", "D2", "F2", "H2"],
  );
  assert.equal(f.writes[3].value, "Video ngắn");
});
test("a formula returning blank prevents every write before the first mutation", async () => {
  const f = await sheetFixture(["", '=""', "", ""]);
  await assert.rejects(
    f.context.writeSheet(f.driver, f.job, async () => {}),
    /công thức/,
  );
  assert.equal(f.writes.length, 0);
});
test("human edits between planning and writing are preserved and stop the operation", async () => {
  const f = await sheetFixture();
  await assert.rejects(
    f.context.writeSheet(f.driver, f.job, async (info) => {
      if (info.sheetRow && !info.sheetColumn) f.cells.set("D2", "Human edit");
    }),
    /dữ liệu|thay đổi/,
  );
  assert.equal(f.cells.get("D2"), "Human edit");
  assert.equal(f.writes.length, 0);
});
test("partial browser write reuses its row and skips already-correct cells", async () => {
  const f = await sheetFixture([
    "https://www.douyin.com/video/7663523864474930442",
    "Hố đen",
    "",
    "",
  ]);
  f.job.pendingRow = 2;
  const result = await f.context.writeSheet(f.driver, f.job, async () => {});
  assert.equal(result.row, 2);
  assert.deepEqual(
    f.writes.map((x) => x.address),
    ["F2", "H2"],
  );
});
test("extension ignores messages from unrelated websites and subframes", async () => {
  let listener,
    registered = 0;
  const context = {
    importScripts: () => {},
    chrome: {
      runtime: {
        onMessage: {
          addListener: (fn) => {
            listener = fn;
          },
        },
        onStartup: { addListener: () => {} },
      },
      storage: {
        local: {
          get: async () => {
            registered++;
            return {};
          },
          set: async () => {},
        },
      },
    },
    crypto: { randomUUID: () => "browser-id" },
    fetch: () => {
      throw new Error("Should not fetch");
    },
  };
  await load(context, ["background-v2.js"]);
  for (const sender of [
    { url: "https://evil.com", tab: { id: 1 }, frameId: 0 },
    { url: "http://127.0.0.1:43127/", tab: { id: 1 }, frameId: 1 },
  ])
    listener(
      { type: "attach-app", token: "a".repeat(64), sessionId: "s".repeat(30) },
      sender,
      () => {},
    );
  assert.equal(registered, 0);
});
test("manifest limits automation to task domains and installs no recurring search alarm", async () => {
  const manifest = JSON.parse(
    await fs.readFile(
      new URL("../../extension/manifest.json", import.meta.url),
      "utf8",
    ),
  );
  assert.equal(manifest.version, "0.2.1");
  assert.ok(manifest.permissions.includes("debugger"));
  assert.equal(manifest.permissions.includes("alarms"), false);
  assert.equal(manifest.host_permissions.includes("<all_urls>"), false);
  assert.equal(
    manifest.host_permissions.some((x) => x.includes("accounts.google.com")),
    false,
  );
  assert.equal(
    manifest.content_scripts.some((x) => x.js.includes("content.js")),
    false,
  );
});
