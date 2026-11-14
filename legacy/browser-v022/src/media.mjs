import fs from "node:fs/promises";
import path from "node:path";
import { Readable } from "node:stream";
import { pipeline } from "node:stream/promises";
import { createWriteStream } from "node:fs";
import { spawn } from "node:child_process";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";
import { folderName, compareMedia, mediaArea } from "./domain.mjs";
const defaultStateDir = fileURLToPath(
  new URL("../data/downloads/", import.meta.url),
);
export function allowedMedia(url) {
  try {
    const u = new URL(url);
    return (
      u.protocol === "https:" &&
      !u.username &&
      !u.password &&
      !u.port &&
      /(^|\.)(douyinvod\.com|douyinpic\.com|byteimg\.com|bytecdn\.cn|douyin\.com|ibytedtos\.com|bytedance\.com|douyinstatic\.com|zjcdn\.com)$/.test(
        u.hostname,
      )
    );
  } catch {
    return false;
  }
}
export async function download(url, file, signal) {
  for (let i = 0; i < 6; i++) {
    if (!allowedMedia(url))
      throw new Error(
        "Địa chỉ media chưa nằm trong danh sách máy chủ Douyin được hỗ trợ.",
      );
    const r = await fetch(url, {
      redirect: "manual",
      headers: {
        Referer: "https://www.douyin.com/",
        "User-Agent": "Mozilla/5.0",
      },
      signal,
    });
    if (r.status >= 300 && r.status < 400) {
      url = new URL(r.headers.get("location"), url).href;
      await r.body?.cancel();
      continue;
    }
    if (!r.ok || !r.body)
      throw new Error("Tải media thất bại: HTTP " + r.status);
    const type = r.headers.get("content-type") || "";
    if (type.includes("text/html"))
      throw new Error("Máy chủ trả trang xác minh thay vì media.");
    const partial = file + ".part";
    try {
      await pipeline(
        Readable.fromWeb(r.body),
        createWriteStream(partial, { flags: "w" }),
        { signal },
      );
      await fs.rename(partial, file);
    } catch (e) {
      await fs.rm(partial, { force: true });
      throw e;
    }
    return { type, url };
  }
  throw new Error("Media chuyển hướng quá nhiều lần.");
}
export async function probeMediaSource(m, signal) {
  if (!allowedMedia(m.url)) throw new Error("Nguồn video không thuộc Douyin.");
  return JSON.parse(
    await runProcess(
      "ffprobe",
      [
        "-v",
        "error",
        "-rw_timeout",
        "8000000",
        "-headers",
        "Referer: https://www.douyin.com/\r\nUser-Agent: Mozilla/5.0\r\n",
        "-show_entries",
        "stream=codec_type,codec_name,width,height,bit_rate:format=duration",
        "-of",
        "json",
        m.url,
      ],
      { signal, timeout: 12000 },
    ),
  );
}
export async function downloadVideo(v, file, signal, dependencies = {}) {
  const fetchMedia = dependencies.download || download;
  const probe =
    dependencies.probe ||
    (async (file) =>
      JSON.parse(
        await runProcess(
          "ffprobe",
          [
            "-v",
            "error",
            "-show_entries",
            "stream=codec_type,codec_name,width,height:format=duration",
            "-of",
            "json",
            file,
          ],
          { signal },
        ),
      ));
  const sources = (v.media || []).map((m) => ({ ...m }));
  const probeSource =
    dependencies.probeSource || ((m) => probeMediaSource(m, signal));
  let last,
    best,
    failures = [];
  const checkedGroups = new Map();
  for (const m of sources.filter((m) => !mediaArea(m))) {
    signal?.throwIfAborted();
    const key = m.sourceGroup || m.url;
    if (!checkedGroups.has(key)) {
      try {
        const info = await probeSource(m);
        const stream = info.streams?.find((s) => s.codec_type === "video");
        checkedGroups.set(
          key,
          stream?.width && stream?.height
            ? {
                width: stream.width,
                height: stream.height,
                codec: stream.codec_name,
                bitrate: Number(stream.bit_rate) || m.bitrate,
              }
            : null,
        );
      } catch (e) {
        checkedGroups.set(key, null);
        failures.push("Không đọc được thông tin nguồn bổ sung: " + e.message);
      }
    }
    if (checkedGroups.get(key))
      Object.assign(m, checkedGroups.get(key), { dimensionsSource: "probe" });
  }
  // Unknown addresses are verified before accepting a known rendition as best.
  sources.sort(
    (a, b) =>
      Number(!mediaArea(b)) - Number(!mediaArea(a)) || compareMedia(a, b),
  );
  let targetArea = Math.max(0, ...sources.map(mediaArea));
  const completedGroups = new Set();
  const candidate = file + ".candidate.mp4";
  for (let index = 0; index < sources.length; index++) {
    const m = sources[index];
    if (m.sourceGroup && completedGroups.has(m.sourceGroup)) continue;
    try {
      await fetchMedia(m.url, candidate, signal);
      const info = await probe(candidate);
      const stream = info.streams?.find((x) => x.codec_type === "video");
      if (!stream || !info.streams.some((x) => x.codec_type === "audio"))
        throw new Error("File tải về chưa có đủ hình và âm thanh.");
      const seconds = Number(info.format?.duration);
      if (
        v.duration &&
        (!Number.isFinite(seconds) ||
          Math.abs(seconds - v.duration) > Math.max(3, v.duration * 0.02))
      )
        throw new Error("Thời lượng file tải không khớp video.");
      const detail = {
        bitrate: m.bitrate,
        width: stream.width,
        height: stream.height,
        codec: stream.codec_name,
        sourceKind: m.sourceKind || "playback",
        sourceField: m.sourceField || "",
        originalVerified: false,
        duration: seconds,
        description:
          "Bản phát tốt nhất tải được trong các nguồn trình duyệt đã quan sát; chưa xác minh file gốc.",
      };
      if (!best || compareMedia(detail, best) < 0) {
        await fs.rename(candidate, file);
        best = detail;
      } else await fs.rm(candidate, { force: true });
      if (m.sourceGroup && (!mediaArea(m) || mediaArea(detail) >= mediaArea(m)))
        completedGroups.add(m.sourceGroup);
      if (mediaArea(detail) > targetArea) targetArea = mediaArea(detail);
      const remainingUnknown = sources
        .slice(index + 1)
        .some(
          (x) =>
            !mediaArea(x) &&
            (!x.sourceGroup || !completedGroups.has(x.sourceGroup)),
        );
      const remainingBetter = sources
        .slice(index + 1)
        .some(
          (x) =>
            (!x.sourceGroup || !completedGroups.has(x.sourceGroup)) &&
            compareMedia({ ...x, codec: x.codec || best.codec }, best) < 0,
        );
      if (
        !remainingUnknown &&
        !remainingBetter &&
        mediaArea(detail) >= targetArea
      )
        return { ...best, targetArea, failures };
      if (mediaArea(m) && mediaArea(detail) < mediaArea(m))
        failures.push(
          "Nguồn ghi " +
            m.width +
            "×" +
            m.height +
            " nhưng file thực tế " +
            detail.width +
            "×" +
            detail.height,
        );
    } catch (e) {
      last = e;
      failures.push(e.message);
      await fs.rm(candidate, { force: true });
      if (signal?.aborted) {
        await fs.rm(file, { force: true });
        throw e;
      }
    }
  }
  if (best)
    return {
      ...best,
      targetArea,
      failures,
      warning:
        "Chỉ tải được " +
        best.width +
        "×" +
        best.height +
        "; nguồn độ phân giải cao hơn đã lỗi hoặc không khớp nhãn.",
    };
  throw (
    last ||
    new Error(
      "Chưa lấy được nguồn video. Mở lại video qua extension để làm mới liên kết.",
    )
  );
}
export async function saveVideo(video, title, root, signal, options = {}) {
  if (!path.isAbsolute(root || "")) throw new Error("Chọn thư mục lưu đầy đủ.");
  const stateDir = path.join(
    options.stateDir || path.dirname(defaultStateDir),
    "video-downloads",
  );
  await fs.mkdir(stateDir, { recursive: true });
  const key = crypto
    .createHash("sha256")
    .update(path.resolve(root) + "\0" + video.id + "\0" + folderName(title))
    .digest("hex");
  const journal = path.join(stateDir, key + ".json");
  let report;
  try {
    report = JSON.parse(await fs.readFile(journal, "utf8"));
  } catch (e) {
    if (e.code !== "ENOENT") throw e;
  }
  const probe =
    options.probe ||
    (async (file) =>
      JSON.parse(
        await runProcess(
          "ffprobe",
          [
            "-v",
            "error",
            "-show_entries",
            "stream=codec_type,width,height:format=duration",
            "-of",
            "json",
            file,
          ],
          { signal, timeout: 15000 },
        ),
      ));
  const inside = (file) => {
    const relative = path.relative(path.resolve(root), path.resolve(file));
    return relative && !relative.startsWith("..") && !path.isAbsolute(relative);
  };
  if (report?.id !== video.id) report = null;
  if (report?.file && !inside(report.file))
    throw new Error("Checkpoint nằm ngoài thư mục lưu đã chọn.");
  if (report?.ok) {
    try {
      const stat = await fs.stat(report.file),
        info = await probe(report.file);
      const seconds = Number(info.format?.duration);
      if (
        stat.size > 0 &&
        info.streams?.some((s) => s.codec_type === "video") &&
        info.streams?.some((s) => s.codec_type === "audio") &&
        Math.abs(seconds - video.duration) <= Math.max(3, video.duration * 0.02)
      )
        return report;
    } catch (e) {
      if (signal?.aborted) throw e;
    }
    report.ok = false;
  }
  if (!report) {
    let dir = path.join(root, folderName(title)),
      suffix = 1;
    while (true) {
      try {
        await fs.mkdir(dir);
        break;
      } catch (e) {
        if (e.code !== "EEXIST") throw e;
        dir = path.join(root, folderName(title) + " " + ++suffix);
      }
    }
    report = {
      id: video.id,
      dir,
      file: path.join(dir, folderName(title) + ".mp4"),
      ok: false,
    };
  }
  const persist = async () => {
    await fs.writeFile(journal + ".tmp", JSON.stringify(report, null, 2));
    await fs.rename(journal + ".tmp", journal);
  };
  await persist();
  signal?.throwIfAborted();
  const detail = await (options.downloadVideo || downloadVideo)(
    video,
    report.file,
    signal,
    options.dependencies,
  );
  signal?.throwIfAborted();
  Object.assign(report, detail, { ok: true });
  await persist();
  return report;
}
export function runProcess(exe, args, { signal, timeout = 1800000 } = {}) {
  return new Promise((resolve, reject) => {
    const p = spawn(exe, args, { windowsHide: true, signal });
    let out = "",
      err = "";
    const timer = setTimeout(() => {
      p.kill();
      reject(new Error("Tiến trình xử lý hết thời gian."));
    }, timeout);
    p.stdout.on("data", (d) => (out += d));
    p.stderr.on("data", (d) => (err = (err + d).slice(-5000)));
    p.on("error", (e) => {
      clearTimeout(timer);
      reject(e);
    });
    p.on("exit", (code) => {
      clearTimeout(timer);
      code === 0
        ? resolve(out)
        : reject(new Error("Xử lý media thất bại: " + err.slice(-700)));
    });
  });
}
