importScripts(
  "browser-driver.js",
  "sheet-policy.js",
  "sheets-browser.js",
  "youtube-studio.js",
);
const BASE = "http://127.0.0.1:43127";
let busy = false,
  again = false,
  owner = null,
  token = null;
async function request(path, data) {
  const response = await fetch(BASE + path, {
    method: data ? "POST" : "GET",
    headers: {
      Authorization: "Bearer " + token,
      "Content-Type": "application/json",
      "X-Browser-Id": owner?.browserId || "",
      "X-App-Session": owner?.sessionId || "",
      "X-Video-Extension-Version": "0.2.1",
    },
    body: data ? JSON.stringify(data) : undefined,
    signal: AbortSignal.timeout(20000),
  });
  const body = await response.json();
  if (!response.ok) throw new Error(body.error || "Chưa kết nối ứng dụng.");
  return body;
}
async function makeDriver(url, job) {
  let tab;
  if (job.tabId) {
    try {
      const old = await chrome.tabs.get(job.tabId);
      if (new URL(old.url).origin === new URL(url).origin) tab = old;
    } catch {}
  }
  if (!tab) tab = await chrome.tabs.create({ url, active: true });
  const alive = async () => {
    const state = await request("/bridge/active", {
      jobId: job.id,
      lease: job.lease,
    });
    return state.active;
  };
  const progress = async (info) => {
    const state = await request("/bridge/progress", {
      jobId: job.id,
      lease: job.lease,
      tabId: tab.id,
      ...info,
    });
    if (!state.ok)
      throw new Attention("Công việc đã dừng. Giữ tab để kiểm tra.");
  };
  let driver;
  for (let i = 0; i < 30; i++) {
    const current = await chrome.tabs.get(tab.id);
    if (current.status === "complete") break;
    await progress({ message: "Đang mở trang trong đúng profile." });
    await new Promise((r) => setTimeout(r, 500));
  }
  driver = new BrowserDriver(tab.id, new URL(url).origin, alive, progress);
  await driver.attach();
  await progress({ message: "Đang xử lý trong đúng profile." });
  return { driver, progress, tab };
}
async function preflight(job) {
  let channelId = job.channel.id,
    channelName = "";
  if (!channelId) {
    const { driver } = await makeDriver(job.channel.url, job);
    try {
      channelId = await driver.wait(
        () => driver.page(channelPageId),
        (x) => /^UC[\w-]{22}$/.test(x || ""),
        "Đang nhận diện link kênh YouTube.",
      );
    } finally {
      await driver.detach();
    }
  }
  const { driver: sheet } = await makeDriver(
    "https://docs.google.com/spreadsheets/d/" + job.sheetId + "/edit",
    job,
  );
  try {
    await openSheet(sheet, job.sheetTab);
  } finally {
    await sheet.detach();
  }
  const { driver: studio } = await makeDriver(
    "https://studio.youtube.com/channel/" + channelId,
    job,
  );
  try {
    channelName = (await verifyStudio(studio, channelId)).name;
  } finally {
    await studio.detach();
  }
  return { sheetReady: true, channelId, channelName };
}
async function detail(job) {
  const tab = await chrome.tabs.create({ url: job.url, active: true });
  let result;
  const progress = (info) =>
    request("/bridge/progress", {
      jobId: job.id,
      lease: job.lease,
      tabId: tab.id,
      ...info,
    });
  for (let i = 0; i < 60; i++) {
    if (
      !(await request("/bridge/active", { jobId: job.id, lease: job.lease }))
        .active
    )
      throw new Attention("Đã dừng đọc Douyin.");
    try {
      result = await chrome.tabs.sendMessage(tab.id, {
        type: "read-video",
        videoId: job.videoId,
      });
    } catch {}
    if (result?.attention) throw new Attention(result.message);
    if (result?.video) {
      await chrome.tabs
        .sendMessage(tab.id, { type: "highest-quality" })
        .catch(() => {});
      await new Promise((r) => setTimeout(r, 1000));
      const higher = await chrome.tabs.sendMessage(tab.id, {
        type: "read-video",
        videoId: job.videoId,
      });
      return { video: higher?.video || result.video };
    }
    await progress({
      message: "Đang chờ thời lượng và nguồn phát của đúng video Douyin.",
    });
    await new Promise((r) => setTimeout(r, 750));
  }
  throw new Attention(
    "Chưa lấy được nguồn/thời lượng. Mở và phát video Douyin trong tab này rồi Tiếp tục.",
  );
}
async function run(job) {
  if (job.kind === "preflight") return preflight(job);
  if (job.kind === "detail") return detail(job);
  const url =
    job.kind === "sheet"
      ? "https://docs.google.com/spreadsheets/d/" + job.sheetId + "/edit"
      : job.kind === "youtube-confirm"
        ? "https://studio.youtube.com/video/" + job.youtubeId + "/edit"
        : "https://studio.youtube.com/channel/" + job.channelId;
  const { driver, progress } = await makeDriver(url, job);
  try {
    if (job.kind === "sheet")
      return { sheet: await writeSheet(driver, job, progress) };
    if (job.kind === "youtube-confirm")
      return { youtube: await studioConfirm(driver, job, progress) };
    if (job.kind === "youtube") {
      const result = await uploadStudio(driver, job, progress);
      if (result.needsVerification) {
        await driver.detach();
        await chrome.tabs.update(driver.tabId, {
          url: "https://studio.youtube.com/video/" + result.id + "/edit",
        });
        const confirm = await makeDriver(
          "https://studio.youtube.com/video/" + result.id + "/edit",
          { ...job, tabId: driver.tabId },
        );
        try {
          return {
            youtube: await studioConfirm(
              confirm.driver,
              { ...job, youtubeId: result.id },
              confirm.progress,
            ),
          };
        } finally {
          await confirm.driver.detach();
        }
      }
    }
    throw new Error("Công việc trình duyệt không hợp lệ.");
  } finally {
    await driver.detach();
  }
}
async function poll() {
  if (busy) {
    again = true;
    return;
  }
  if (!owner || !token) return;
  busy = true;
  try {
    do {
      again = false;
      await request("/bridge/register", owner);
      const job = await request("/bridge/job");
      if (!job.id) break;
      try {
        const result = await run(job);
        await request("/bridge/result", {
          jobId: job.id,
          lease: job.lease,
          ...result,
        });
      } catch (error) {
        await request("/bridge/result", {
          jobId: job.id,
          lease: job.lease,
          attention: true,
          message: error.message,
        }).catch(() => {});
        break;
      }
      again = true;
    } while (again);
  } catch (error) {
    await chrome.storage.local.set({ lastStatus: error.message });
  } finally {
    busy = false;
    if (again) void poll();
  }
}
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  const origin = (() => {
    try {
      return new URL(sender.url).origin;
    } catch {
      return "";
    }
  })();
  if (origin !== BASE || sender.frameId !== 0 || !sender.tab) return;
  if (message.type === "attach-app") {
    (async () => {
      try {
        if (
          !/^[a-f0-9]{64}$/.test(message.token) ||
          !/^[\w-]{20,80}$/.test(message.sessionId)
        )
          throw new Error("Ghép nối không hợp lệ.");
        const saved = await chrome.storage.local.get(["browserId"]);
        const browserId = saved.browserId || crypto.randomUUID();
        await chrome.storage.local.set({ browserId });
        if (busy && owner?.sessionId !== message.sessionId)
          throw new Error("Đang chạy từ tab ứng dụng khác trong profile này.");
        owner = { browserId, sessionId: message.sessionId };
        token = message.token;
        await request("/bridge/register", owner);
        reply({ ok: true, browserId, version: "0.2.1" });
        void poll();
      } catch (error) {
        reply({ ok: false, message: error.message });
      }
    })();
    return true;
  }
  if (message.type === "wake" && message.sessionId === owner?.sessionId) {
    void poll();
    reply({ ok: true });
  }
});
chrome.runtime.onStartup.addListener(() => {});
