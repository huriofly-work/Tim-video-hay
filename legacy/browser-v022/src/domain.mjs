export function videoId(value) {
  if (typeof value === "string" && /^=HYPERLINK\(/i.test(value))
    value = value.match(/^=HYPERLINK\("([^"]+)"/i)?.[1];
  if (/^\d{15,22}$/.test(String(value))) return String(value);
  try {
    const u = new URL(value);
    if (
      u.protocol !== "https:" ||
      u.username ||
      u.password ||
      u.port ||
      ![
        "www.douyin.com",
        "douyin.com",
        "www.iesdouyin.com",
        "iesdouyin.com",
      ].includes(u.hostname)
    )
      return null;
    return (
      u.pathname.match(/^\/(?:video|share\/video)\/(\d{15,22})\/?$/)?.[1] ||
      u.searchParams.get("modal_id")?.match(/^\d{15,22}$/)?.[0] ||
      null
    );
  } catch {
    return null;
  }
}
export const canonical = (id) => "https://www.douyin.com/video/" + id;
export const videoType = (seconds) =>
  Number.isFinite(seconds) && seconds >= 0
    ? seconds > 240
      ? "Long"
      : "Shorts"
    : null;
export const text = (value, max = 10000) =>
  typeof value === "string" ? value.slice(0, max) : "";
export const count = (value) =>
  typeof value === "number" && Number.isFinite(value) && value >= 0
    ? value
    : null;
export function shortTitle(value) {
  const name = text(value)
    .normalize("NFC")
    .replace(/[^\p{L}\p{N}\s]/gu, " ")
    .replace(/\s+/g, " ")
    .trim();
  return /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/i.test(name)
    ? "Video " + name
    : name;
}
export const folderName = (value) => shortTitle(value) || "Video";
export const mediaArea = (m) => (m.width || 0) * (m.height || 0);
export const compareMedia = (a, b) =>
  mediaArea(b) - mediaArea(a) ||
  (a.sourceKind === "download" ? 1 : 0) -
    (b.sourceKind === "download" ? 1 : 0) ||
  (!a.codec || !b.codec || a.codec === b.codec
    ? (b.bitrate || 0) - (a.bitrate || 0)
    : 0);
export function normalizeVideo(raw) {
  const id = videoId(raw.aweme_id || raw.id || raw.url);
  if (!id) throw new Error("ID video Douyin không hợp lệ.");
  const v = raw.video || {};
  const media = [];
  for (const b of (v.bit_rate || []).slice(0, 50))
    for (const url of b.play_addr?.url_list || [])
      media.push({
        url,
        bitrate: count(b.bit_rate) || 0,
        width: count(b.play_addr.width),
        height: count(b.play_addr.height),
        codec: b.is_h265 ? "hevc" : b.codec_type || "",
        dimensionsSource: "variant",
        sourceKind: "playback",
        sourceField: "bit_rate.play_addr",
        sourceGroup: `variant:${b.bit_rate || 0}:${b.play_addr.width || 0}:${b.play_addr.height || 0}:${b.is_h265 || b.codec_type || 0}`,
      });
  for (const url of v.play_addr?.url_list || [])
    media.push({
      url,
      bitrate: 0,
      width: count(v.width),
      height: count(v.height),
      dimensionsSource: "video-default",
      sourceKind: "playback",
      sourceField: "play_addr",
      sourceGroup: "play_addr",
    });
  // Keep alternative addresses only when they are actually supplied by the page.
  // An address named download/original is not proof of the uploader's original file.
  for (const [field, address] of Object.entries(v)) {
    if (
      field === "play_addr" ||
      !/(?:play|download|original|origin).*addr/i.test(field) ||
      !Array.isArray(address?.url_list)
    )
      continue;
    for (const url of address.url_list.slice(0, 6)) {
      if (typeof url !== "string") continue;
      media.push({
        url,
        bitrate: count(address.bit_rate) || 0,
        width: count(address.width),
        height: count(address.height),
        codec: /265|hevc/i.test(field)
          ? "hevc"
          : /264/i.test(field)
            ? "h264"
            : text(address.codec_type, 40),
        dimensionsSource: "address",
        sourceKind: /download/i.test(field) ? "download" : "playback",
        sourceField: field,
        sourceGroup: field,
      });
    }
  }
  for (const m of (raw.media || []).slice(0, 50))
    if (m && typeof m.url === "string")
      media.push({
        url: m.url,
        bitrate: count(m.bitrate) || 0,
        width: count(m.width),
        height: count(m.height),
        codec: text(m.codec, 40),
        dimensionsSource: m.dimensionsSource || "observed",
        sourceKind: text(m.sourceKind, 40) || "playback",
        sourceField: text(m.sourceField, 100),
        sourceGroup: text(m.sourceGroup, 200),
      });
  return {
    id,
    url: canonical(id),
    duration:
      count(raw.duration) ??
      (count(v.duration) === null ? null : v.duration / 1000),
    media: [
      ...media
        .reduce((map, m) => {
          const old = map.get(m.url);
          if (
            !old ||
            (old.dimensionsSource === "video-default" &&
              m.dimensionsSource !== "video-default")
          )
            map.set(m.url, m);
          return map;
        }, new Map())
        .values(),
    ].sort(compareMedia),
  };
}
