const $ = (id) => document.getElementById(id);
let sessionId = sessionStorage.getItem("videoAppSession");
if (!sessionId) {
  sessionId = crypto.randomUUID();
  sessionStorage.setItem("videoAppSession", sessionId);
}
let browserId = "",
  state = null,
  settingsLoaded = false,
  refreshing = false,
  folderPath = "",
  folderTarget = "outputDir";
const params = () => new URLSearchParams({ sessionId, browserId });
async function api(route, data) {
  const response = await fetch(
    route + (route.includes("?") ? "&" : "?") + params(),
    {
      method: data ? "POST" : "GET",
      headers: data ? { "Content-Type": "application/json" } : {},
      body: data ? JSON.stringify(data) : undefined,
      cache: "no-store",
    },
  );
  let body;
  try {
    body = await response.json();
  } catch {
    throw new Error("Máy chủ chưa trả dữ liệu. Mở lại ứng dụng.");
  }
  if (!response.ok) throw new Error(body.error || "Không thực hiện được.");
  return body;
}
function tell(value, target = "notice") {
  $(target).textContent = value;
}
function wake(type = "wake") {
  window.postMessage(
    { source: "video-app-v2", type, sessionId },
    location.origin,
  );
}
window.addEventListener("message", (event) => {
  if (
    event.source !== window ||
    event.origin !== location.origin ||
    event.data?.source !== "video-extension-v2" ||
    event.data.sessionId !== sessionId
  )
    return;
  if (event.data.ok) {
    browserId = event.data.browserId;
    void refresh();
  } else tell(event.data.message || "Chưa kết nối tiện ích.");
});
const node = (tag, value) => {
  const el = document.createElement(tag);
  el.textContent = value;
  return el;
};
function link(parent, label, url) {
  const el = node("a", label);
  el.href = url;
  el.target = "_blank";
  el.rel = "noopener";
  parent.append(el);
  parent.append(document.createTextNode(" "));
}
function render() {
  $("connection").textContent = state.connected
    ? "Đã gắn tiện ích với đúng profile"
    : "Chưa kết nối tiện ích 0.2.1 trong profile này";
  if (!settingsLoaded) {
    for (const key of ["channelUrl", "outputDir", "sheetTab"])
      $(key).value = state.settings[key] || "";
    settingsLoaded = true;
    $("downloadDir").value =
      state.settings.downloadOutputDir || state.settings.outputDir || "";
    $("sheetUrl").value =
      "https://docs.google.com/spreadsheets/d/" +
      encodeURIComponent(state.settings.sheetId) +
      "/edit";
  }
  $("sheetInfo").textContent =
    "Sheets: " +
    state.settings.sheetTab +
    " · Chỉ ghi Link video mẫu, Tên video, Ngách, Loại video";
  const t = state.current,
    active = t && !["complete", "stopped", "error"].includes(t.status);
  $("start").disabled = !state.connected || active;
  $("sheetSave").disabled = t?.status === "running";
  $("sheetLink").href =
    "https://docs.google.com/spreadsheets/d/" +
    encodeURIComponent(state.settings.sheetId) +
    "/edit";
  $("sheetLink").textContent =
    "Mở bảng đang cấu hình · " + state.settings.sheetTab;
  $("progress").hidden = !t;
  if (t) {
    $("taskTitle").textContent = t.title + " · " + t.niche;
    $("taskStatus").textContent = t.message;
    $("steps").replaceChildren();
    $("steps").className = "steps";
    for (const [label, result] of [
      ["Google Sheets", t.sheet],
      ["Tải video", t.download],
      ["YouTube", t.youtube],
    ]) {
      const el = node(
        "span",
        label +
          ": " +
          (result?.ok || result?.visibility === "unlisted"
            ? "Hoàn tất"
            : result?.id
              ? "Có bản nháp"
              : "Chờ"),
      );
      el.className = "step";
      $("steps").append(el);
    }
    $("resume").disabled =
      !t.canResume || !["error", "attention", "stopped"].includes(t.status);
    $("stop").disabled = !t.canControl || t.status === "complete";
    $("reconcile").hidden = !t.outcomeUnknown || !!t.youtube?.id;
    $("resultLinks").replaceChildren();
    if (t.youtube?.url)
      link($("resultLinks"), "Mở video YouTube", t.youtube.url);
    if (t.source?.url) link($("resultLinks"), "Mở video Douyin", t.source.url);
    if (t.download?.file)
      $("resultLinks").append(node("p", "File: " + t.download.file));
    if (t.download?.width && t.download?.height)
      $("resultLinks").append(
        node(
          "p",
          "Video đã tải: " + t.download.width + " × " + t.download.height,
        ),
      );
    if (t.download?.warning)
      $("resultLinks").append(node("p", t.download.warning));
  }
  $("history").replaceChildren();
  for (const item of state.history) {
    const el = node("div", "");
    el.className = "entry";
    el.append(
      node("strong", item.title + " · " + item.niche),
      node("p", item.message),
    );
    if (item.youtube?.url) link(el, "YouTube", item.youtube.url);
    $("history").append(el);
  }
  if (!state.history.length)
    $("history").append(node("p", "Chưa có công việc."));
  renderDownloads();
}
function renderDownloads() {
  const d = state.downloadOnly,
    t = d?.current;
  const active = t && !["complete", "stopped", "error"].includes(t.status);
  $("downloadStart").disabled = !d?.connected || active;
  $("downloadProgress").hidden = !t;
  if (t) {
    $("downloadTaskTitle").textContent = t.title;
    $("downloadTaskStatus").textContent = t.message;
    $("downloadResume").disabled =
      !d.connected ||
      !t.canResume ||
      !["error", "attention", "stopped"].includes(t.status);
    $("downloadStop").disabled =
      !t.canControl || ["complete", "stopped"].includes(t.status);
    $("downloadResult").replaceChildren();
    if (t.source?.url)
      link($("downloadResult"), "Mở video Douyin", t.source.url);
    if (t.download?.file)
      $("downloadResult").append(node("p", "File: " + t.download.file));
    if (t.download?.width && t.download?.height)
      $("downloadResult").append(
        node(
          "p",
          "Video đã tải: " + t.download.width + " × " + t.download.height,
        ),
      );
    if (t.download?.warning)
      $("downloadResult").append(node("p", t.download.warning));
  }
  $("downloadHistory").replaceChildren();
  for (const item of d?.history || []) {
    const el = node("div", "");
    el.className = "entry";
    el.append(node("strong", item.title), node("p", item.message));
    if (item.download?.file)
      el.append(node("p", "File: " + item.download.file));
    $("downloadHistory").append(el);
  }
  if (!d?.history?.length)
    $("downloadHistory").append(node("p", "Chưa có lượt tải riêng."));
}
async function refresh() {
  if (refreshing) return;
  refreshing = true;
  try {
    state = await api("/api/state");
    render();
  } catch (error) {
    $("connection").textContent = "Mất kết nối ứng dụng trên máy";
    tell(error.message);
  } finally {
    refreshing = false;
  }
}
async function action(route, data = {}, noticeTarget = "notice") {
  try {
    tell("Đang xử lý…", noticeTarget);
    await api(route, data);
    tell("", noticeTarget);
    await refresh();
    wake();
    return true;
  } catch (error) {
    tell(error.message, noticeTarget);
    return false;
  }
}
$("settingsForm").addEventListener("submit", (event) => {
  event.preventDefault();
  void action("/api/settings", {
    channelUrl: $("channelUrl").value,
    outputDir: $("outputDir").value,
  });
});
$("sheetSettingsForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const ok = await action(
    "/api/sheets",
    { sheetUrl: $("sheetUrl").value, sheetTab: $("sheetTab").value },
    "sheetNotice",
  );
  if (ok) {
    $("sheetUrl").value =
      "https://docs.google.com/spreadsheets/d/" +
      encodeURIComponent(state.settings.sheetId) +
      "/edit";
    $("sheetTab").value = state.settings.sheetTab;
    tell(
      "Đã lưu bảng Google Sheets. Áp dụng cho công việc mới.",
      "sheetNotice",
    );
  }
});
for (const field of ["title", "downloadTitle"])
  $(field).addEventListener("input", () => {
    $(field).value = $(field)
      .value.normalize("NFC")
      .replace(/[^\p{L}\p{N}\s]/gu, " ")
      .replace(/\s+/g, " ");
  });
$("downloadForm").addEventListener("submit", (event) => {
  event.preventDefault();
  void action(
    "/api/download/start",
    {
      url: $("downloadUrl").value,
      title: $("downloadTitle").value,
      outputDir: $("downloadDir").value,
    },
    "downloadNotice",
  );
});
$("downloadStop").addEventListener(
  "click",
  () => void action("/api/download/stop", {}, "downloadNotice"),
);
$("downloadResume").addEventListener(
  "click",
  () => void action("/api/download/resume", {}, "downloadNotice"),
);
$("videoForm").addEventListener("submit", (event) => {
  event.preventDefault();
  void action("/api/start", {
    url: $("sourceUrl").value,
    title: $("title").value,
    niche: $("niche").value,
  });
});
$("stop").addEventListener("click", () => void action("/api/stop"));
$("resume").addEventListener("click", () => void action("/api/resume"));
$("linkResult").addEventListener(
  "click",
  () => void action("/api/reconcile", { url: $("youtubeResult").value }),
);
$("notCreated").addEventListener(
  "click",
  () => void action("/api/reconcile", { notCreated: true }),
);
async function folders(value) {
  try {
    const result = await api("/api/folders?path=" + encodeURIComponent(value));
    folderPath = result.path;
    $("folderPath").textContent = result.path || "Chọn ổ đĩa";
    $("folderParent").dataset.path = result.parent || "";
    $("folderList").replaceChildren();
    for (const item of result.folders) {
      const button = node("button", item.name);
      button.className = "secondary";
      button.addEventListener("click", () => void folders(item.path));
      $("folderList").append(button);
    }
    $("folderPicker").hidden = false;
    $("folderPicker").scrollIntoView({ block: "center", behavior: "smooth" });
  } catch (error) {
    tell(
      error.message,
      folderTarget === "downloadDir" ? "downloadNotice" : "notice",
    );
  }
}
$("browse").addEventListener("click", () => {
  folderTarget = "outputDir";
  void folders($("outputDir").value);
});
$("downloadBrowse").addEventListener("click", () => {
  folderTarget = "downloadDir";
  void folders($("downloadDir").value);
});
$("folderParent").addEventListener(
  "click",
  () => void folders($("folderParent").dataset.path),
);
$("folderSelect").addEventListener("click", () => {
  $(folderTarget).value = folderPath;
  $("folderPicker").hidden = true;
});
$("folderClose").addEventListener("click", () => {
  $("folderPicker").hidden = true;
});
void refresh();
wake("attach");
setInterval(() => {
  wake(browserId ? "wake" : "attach");
  void refresh();
}, 2500);
