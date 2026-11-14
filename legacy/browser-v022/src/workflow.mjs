import crypto from "node:crypto";
import fs from "node:fs/promises";
import { shortTitle, normalizeVideo, videoType } from "./domain.mjs";
import { resolveDouyin } from "./source-douyin.mjs";
import { channelLink, validChannelId } from "./channels.mjs";
import { checkOutput } from "./setup.mjs";
import { saveVideo } from "./media.mjs";
export class Workflow {
  constructor(store, services = {}, { downloadOnly = false } = {}) {
    this.store = store;
    this.services = { resolveDouyin, checkOutput, saveVideo, ...services };
    this.peers = new Map();
    this.work = null;
    this.downloadOnly = downloadOnly;
    this.currentKey = downloadOnly ? "currentDownloadJobId" : "currentJobId";
    const task = this.current;
    if (
      task &&
      !["complete", "stopped", "error", "attention"].includes(task.status)
    ) {
      task.status = "attention";
      task.message =
        "Lần trước bị gián đoạn. Tiếp tục trong đúng profile đã chạy.";
      if (task.phase === "youtube" || task.phase === "youtube-confirm")
        task.outcomeUnknown = true;
      if (task.browserJob) {
        task.browserJob.status = "paused";
        delete task.browserJob.lease;
      }
      store.save();
    }
  }
  get current() {
    return this.store.state.jobs[this.store.state[this.currentKey]] || null;
  }
  checkLease() {
    const t = this.current,
      j = t?.browserJob;
    if (
      t?.status !== "running" ||
      j?.status !== "leased" ||
      Date.now() - (j.touchedAt || 0) <= 45000
    )
      return;
    if (["youtube", "youtube-confirm"].includes(t.phase))
      t.outcomeUnknown = true;
    j.status = "paused";
    delete j.lease;
    this.update(
      "attention",
      "Tiện ích đã mất kết nối khi thao tác. Tải lại tiện ích rồi bấm Tiếp tục trong đúng profile; kiểm tra Studio nếu đang upload.",
    );
  }
  register(owner, version) {
    if (
      !owner?.browserId ||
      !owner?.sessionId ||
      !/^[\w-]{20,80}$/.test(owner.browserId) ||
      !/^[\w-]{20,80}$/.test(owner.sessionId)
    )
      throw new Error("Phiên tiện ích không hợp lệ.");
    this.checkLease();
    const t = this.current,
      previous = t
        ? this.peers.get(t.owner.browserId + "|" + t.owner.sessionId)
        : null;
    if (
      t?.status === "running" &&
      t.owner.browserId === owner.browserId &&
      t.owner.sessionId !== owner.sessionId &&
      (!previous || Date.now() - previous.seen > 20000)
    ) {
      if (
        ["youtube", "youtube-confirm"].includes(t.phase) &&
        t.browserJob?.status === "leased"
      )
        t.outcomeUnknown = true;
      if (t.browserJob) {
        t.browserJob.status = "paused";
        delete t.browserJob.lease;
      }
      this.controller?.abort();
      this.update(
        "attention",
        "Tab ứng dụng trước đã mất kết nối. Bấm Tiếp tục trong profile này để nhận lại công việc.",
      );
    }
    this.peers.set(owner.browserId + "|" + owner.sessionId, {
      ...owner,
      version,
      seen: Date.now(),
    });
  }
  connected(owner) {
    return (
      !!owner &&
      this.peers.get(owner.browserId + "|" + owner.sessionId)?.version ===
        "0.2.1" &&
      Date.now() -
        this.peers.get(owner.browserId + "|" + owner.sessionId).seen <
        20000
    );
  }
  owns(owner) {
    return (
      this.current?.owner.browserId === owner?.browserId &&
      this.current?.owner.sessionId === owner?.sessionId
    );
  }
  update(status, message) {
    const t = this.current;
    if (t) {
      Object.assign(t, {
        status,
        message,
        updatedAt: new Date().toISOString(),
      });
      this.store.save();
    }
  }
  queue(phase, payload = {}) {
    const t = this.current;
    t.phase = phase;
    t.browserJob = {
      id: crypto.randomUUID(),
      kind: phase,
      status: "queued",
      ...payload,
    };
    this.update(
      "running",
      {
        preflight: "Đang kiểm tra đăng nhập Sheets và kênh YouTube.",
        detail: "Đang đọc đúng video Douyin.",
        sheet: "Đang điền bốn cột Google Sheets.",
        youtube: "Đang tải video lên YouTube Studio.",
        "youtube-confirm": "Đang kiểm tra video đã tạo trên YouTube.",
      }[phase],
    );
  }
  async start(input, owner) {
    if (
      this.starting ||
      this.work ||
      (this.current &&
        !["complete", "stopped", "error"].includes(this.current.status))
    )
      throw new Error(
        "Đang có công việc. Hoàn tất hoặc dừng trước khi nhập link mới.",
      );
    if (this.current?.outcomeUnknown)
      throw new Error(
        "Kết quả upload trước chưa rõ. Kiểm tra YouTube Studio và Tiếp tục xác minh trước khi tạo công việc mới.",
      );
    if (!this.connected(owner))
      throw new Error(
        "Cài/tải lại tiện ích 0.2.1 trong đúng profile đang mở ứng dụng.",
      );
    const title = shortTitle(input.title),
      niche = String(input.niche || "")
        .normalize("NFC")
        .trim();
    if (!title || [...title].length > 30)
      throw new Error(
        "Tên video cần từ 1 đến 30 ký tự, chỉ gồm chữ, số và khoảng trắng.",
      );
    if (
      !this.downloadOnly &&
      (!niche || niche.length > 100 || /^[=+@-]/.test(niche))
    )
      throw new Error("Nhập ngách chủ đề hợp lệ, tối đa 100 ký tự.");
    this.starting = true;
    try {
      const channel = this.downloadOnly
        ? null
        : channelLink(this.store.state.settings.channelUrl);
      const outputDir = await this.services.checkOutput(
        this.downloadOnly
          ? String(input.outputDir || "").trim()
          : this.store.state.settings.outputDir,
      );
      const source = await this.services.resolveDouyin(input.url);
      const duplicate =
        !this.downloadOnly &&
        Object.values(this.store.state.jobs).find(
          (t) =>
            t.source.id === source.id &&
            t.settings.channelUrl === channel.url &&
            (t.youtube?.id || t.outcomeUnknown),
        );
      if (!this.downloadOnly && duplicate)
        throw new Error(
          "Video này đã có kết quả hoặc upload chưa xác định trên kênh đã chọn. Xem lịch sử trước khi đăng lại.",
        );
      const t = {
        id: crypto.randomUUID(),
        owner: { ...owner },
        source,
        title,
        niche: this.downloadOnly ? "" : niche,
        mode: this.downloadOnly ? "download" : "pipeline",
        settings: this.downloadOnly
          ? { outputDir }
          : {
              ...this.store.state.settings,
              channelUrl: channel?.url || "",
              outputDir,
            },
        createdAt: new Date().toISOString(),
        sheet: {},
        download: {},
        youtube: {},
        status: "running",
      };
      this.store.state.jobs[t.id] = t;
      this.store.state[this.currentKey] = t.id;
      this.controller = new AbortController();
      if (this.downloadOnly) {
        this.store.state.settings.downloadOutputDir = outputDir;
        this.queue("detail", { url: t.source.url, videoId: t.source.id });
      } else
        this.queue("preflight", {
          sheetId: t.settings.sheetId,
          sheetTab: t.settings.sheetTab,
          channel,
        });
      return t;
    } finally {
      this.starting = false;
    }
  }
  next(owner) {
    const t = this.current,
      j = t?.browserJob;
    if (
      !this.owns(owner) ||
      !this.connected(owner) ||
      t.status !== "running" ||
      !j ||
      j.status !== "queued"
    )
      return {};
    j.status = "leased";
    j.lease = crypto.randomUUID();
    j.touchedAt = Date.now();
    this.store.save();
    return { ...j, taskId: t.id };
  }
  active(owner, jobId, lease) {
    this.checkLease();
    const t = this.current,
      j = t?.browserJob;
    const ok = !!(
      this.owns(owner) &&
      this.connected(owner) &&
      t.status === "running" &&
      j?.id === jobId &&
      j.lease === lease &&
      j.status === "leased"
    );
    if (ok) j.touchedAt = Date.now();
    return ok;
  }
  progress(owner, body) {
    if (!this.active(owner, body.jobId, body.lease)) return false;
    const t = this.current;
    if (body.youtubeId && /^[\w-]{11}$/.test(body.youtubeId))
      t.youtube = {
        ...t.youtube,
        id: body.youtubeId,
        url: "https://www.youtube.com/watch?v=" + body.youtubeId,
      };
    if (body.uploadStarted && ["youtube", "youtube-confirm"].includes(t.phase))
      t.outcomeUnknown = true;
    if (
      Number.isInteger(body.sheetRow) &&
      body.sheetRow > 0 &&
      t.phase === "sheet"
    )
      t.sheet.pendingRow = body.sheetRow;
    if (
      Number.isInteger(body.sheetColumn) &&
      body.sheetColumn >= 0 &&
      t.phase === "sheet"
    )
      t.sheet.pendingColumns = [
        ...(t.sheet.pendingColumns || []),
        body.sheetColumn,
      ].filter((x, i, a) => a.indexOf(x) === i);
    if (body.tabId) t.browserJob.tabId = body.tabId;
    this.update("running", String(body.message || t.message).slice(0, 600));
    return true;
  }
  async receive(owner, body) {
    if (!this.active(owner, body.jobId, body.lease)) return false;
    const t = this.current,
      j = t.browserJob;
    j.status = "done";
    if (body.attention || body.error) {
      if (
        ["youtube", "youtube-confirm"].includes(t.phase) &&
        body.uploadStarted
      )
        t.outcomeUnknown = true;
      this.update(
        body.attention ? "attention" : "error",
        String(body.message || body.error || "Cần kiểm tra."),
      );
      return true;
    }
    if (t.phase === "preflight") {
      if (!body.sheetReady || !validChannelId(body.channelId))
        throw new Error("Chưa xác minh đăng nhập hoặc kênh YouTube.");
      t.channel = { id: body.channelId, name: String(body.channelName || "") };
      const other = Object.values(this.store.state.jobs).find(
        (x) =>
          x !== t &&
          x.source.id === t.source.id &&
          x.channel?.id === t.channel.id &&
          (x.youtube?.id || x.outcomeUnknown),
      );
      if (other) {
        this.update(
          "attention",
          "Video đã có kết quả trên kênh này. Xem lịch sử.",
        );
        return true;
      }
      this.queue("detail", { url: t.source.url, videoId: t.source.id });
    } else if (t.phase === "detail") {
      const video = normalizeVideo(body.video || {});
      if (
        video.id !== t.source.id ||
        !video.media.length ||
        !Number.isFinite(video.duration) ||
        video.duration <= 0
      )
        throw new Error("Chưa có nguồn tải/thời lượng của đúng video.");
      t.video = video;
      t.type = videoType(video.duration);
      if (this.downloadOnly) this.beginDownload();
      else
        this.queue("sheet", {
          sheetId: t.settings.sheetId,
          sheetTab: t.settings.sheetTab,
          videoId: t.source.id,
          url: t.source.url,
          title: t.title,
          niche: t.niche,
          type: t.type,
          pendingRow: t.sheet.pendingRow,
          pendingColumns: t.sheet.pendingColumns || [],
        });
    } else if (t.phase === "sheet") {
      if (
        !body.sheet?.ok ||
        !Number.isInteger(body.sheet.row) ||
        body.sheet.row < 1
      )
        throw new Error("Chưa xác minh bốn ô và trạng thái lưu Sheets.");
      t.sheet = { ...body.sheet, ok: true };
      this.store.save();
      this.beginDownload();
    } else if (["youtube", "youtube-confirm"].includes(t.phase)) {
      if (
        !/^[\w-]{11}$/.test(body.youtube?.id || "") ||
        body.youtube?.visibility !== "unlisted" ||
        body.youtube?.audience !== "not-kids" ||
        body.youtube?.channelId !== t.channel.id
      )
        throw new Error(
          "Chưa xác minh đúng kênh, chế độ không công khai và không dành cho trẻ em.",
        );
      t.youtube = {
        ...body.youtube,
        url: "https://www.youtube.com/watch?v=" + body.youtube.id,
      };
      t.outcomeUnknown = false;
      this.update(
        "complete",
        "Đã ghi Sheets, tải video và lưu YouTube ở chế độ không công khai.",
      );
    }
    return true;
  }
  beginDownload() {
    const t = this.current;
    t.phase = "download";
    this.controller = new AbortController();
    const signal = this.controller.signal;
    this.update(
      "running",
      "Đang tải và kiểm tra file video; không tải bìa/phụ đề.",
    );
    this.work = this.services
      .saveVideo(t.video, t.title, t.settings.outputDir, signal, {
        stateDir: this.store.dir,
      })
      .then((result) => {
        if (signal.aborted || this.current !== t) return;
        t.download = { ...result, ok: true };
        if (this.downloadOnly)
          this.update("complete", "Đã tải và kiểm tra video.");
        else
          this.queue("youtube", {
            channelId: t.channel.id,
            file: result.file,
            audience: "not-kids",
          });
      })
      .catch((error) => {
        if (this.current === t && !signal.aborted)
          this.update("error", error.message);
      })
      .finally(() => {
        this.work = null;
      });
  }
  stop(owner) {
    if (!this.owns(owner))
      throw new Error("Dừng trong đúng tab/profile đã chạy công việc.");
    const t = this.current;
    if (
      ["youtube", "youtube-confirm"].includes(t.phase) &&
      t.browserJob?.status === "leased"
    )
      t.outcomeUnknown = true;
    this.controller?.abort();
    if (t.browserJob) {
      t.browserJob.status = "paused";
      delete t.browserJob.lease;
    }
    this.update(
      "stopped",
      this.downloadOnly
        ? "Đã dừng tải video riêng."
        : "Đã dừng. Upload đang gửi có thể còn bản nháp trên Studio; kiểm tra trước khi đăng lại.",
    );
  }
  async resume(owner) {
    const t = this.current;
    if (
      !t ||
      this.work ||
      !["attention", "error", "stopped"].includes(t.status)
    )
      throw new Error("Không có bước cần tiếp tục.");
    if (t.owner.browserId !== owner.browserId || !this.connected(owner))
      throw new Error("Mở và tiếp tục trong đúng profile trước đó.");
    t.owner = { ...owner };
    if (t.outcomeUnknown) {
      if (!t.youtube?.id)
        throw new Error(
          "Upload chưa rõ kết quả. Kiểm tra tab Studio và nhập link video đã tạo, hoặc xác nhận chưa tạo video.",
        );
      this.queue("youtube-confirm", {
        channelId: t.channel.id,
        youtubeId: t.youtube.id,
        audience: "not-kids",
      });
      return;
    }
    if (t.phase === "download") {
      this.beginDownload();
      return;
    }
    if (t.phase === "youtube") await fs.access(t.download.file);
    const payload = { ...t.browserJob };
    delete payload.id;
    delete payload.lease;
    delete payload.status;
    delete payload.kind;
    delete payload.touchedAt;
    if (t.phase === "sheet") {
      payload.pendingRow = t.sheet.pendingRow;
      payload.pendingColumns = t.sheet.pendingColumns || [];
    }
    if (t.youtube?.id) {
      payload.youtubeId = t.youtube.id;
      this.queue("youtube-confirm", payload);
    } else this.queue(t.phase, payload);
  }
  reconcile(owner, { url, notCreated }) {
    if (
      this.current?.owner.browserId !== owner?.browserId ||
      !this.connected(owner) ||
      !this.current?.outcomeUnknown ||
      this.work ||
      this.current.status === "running"
    )
      throw new Error("Không có kết quả upload cần kiểm tra ở phiên này.");
    const t = this.current;
    t.owner = { ...owner };
    if (url) {
      let u;
      try {
        u = new URL(url);
      } catch {
        throw new Error("Link YouTube không hợp lệ.");
      }
      const id = ["youtube.com", "www.youtube.com"].includes(u.hostname)
        ? u.searchParams.get("v")
        : u.hostname === "youtu.be"
          ? u.pathname.slice(1)
          : null;
      if (u.protocol !== "https:" || !/^[\w-]{11}$/.test(id || ""))
        throw new Error("Nhập link video YouTube đã tạo.");
      t.youtube = { id, url: "https://www.youtube.com/watch?v=" + id };
    } else if (notCreated === true) {
      t.outcomeUnknown = false;
      t.youtube = {};
    } else
      throw new Error(
        "Cần link video hoặc xác nhận đã kiểm tra chưa có video.",
      );
    this.store.save();
  }
  public(owner) {
    this.checkLease();
    const current = this.current;
    const clean = (t) => {
      const { video, browserJob, ...rest } = t;
      return {
        ...rest,
        canResume: t.owner.browserId === owner?.browserId,
        canControl:
          t.owner.browserId === owner?.browserId &&
          t.owner.sessionId === owner?.sessionId,
      };
    };
    return {
      settings: this.store.state.settings,
      connected: this.connected(owner),
      current: current ? clean(current) : null,
      history: Object.values(this.store.state.jobs)
        .filter((t) => (t.mode === "download") === this.downloadOnly)
        .slice(-50)
        .reverse()
        .map(clean),
    };
  }
}
