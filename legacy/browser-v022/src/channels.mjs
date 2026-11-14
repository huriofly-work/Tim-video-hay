const CHANNEL_ID = /^UC[\w-]{22}$/;
export function channelLink(value) {
  let url;
  try {
    url = new URL(String(value || "").trim());
  } catch {
    throw new Error("Nhập link kênh YouTube hợp lệ.");
  }
  if (
    url.protocol !== "https:" ||
    url.username ||
    url.password ||
    url.port ||
    ![
      "youtube.com",
      "www.youtube.com",
      "m.youtube.com",
      "studio.youtube.com",
    ].includes(url.hostname)
  )
    throw new Error("Cần link kênh HTTPS thuộc YouTube.");
  const parts = url.pathname.split("/").filter(Boolean);
  let id = null;
  if (
    parts[0] === "channel" &&
    CHANNEL_ID.test(parts[1] || "") &&
    (parts.length === 2 ||
      (url.hostname === "studio.youtube.com" &&
        parts.length === 3 &&
        parts[2] === "videos"))
  )
    id = parts[1];
  if (id) return { url: "https://www.youtube.com/channel/" + id, id };
  if (
    url.hostname !== "studio.youtube.com" &&
    parts.length === 1 &&
    /^@[^\s/?#]+$/u.test(decodeURIComponent(parts[0]))
  )
    return { url: "https://www.youtube.com/" + parts[0], id: null };
  if (
    url.hostname !== "studio.youtube.com" &&
    parts.length === 2 &&
    ["c", "user"].includes(parts[0]) &&
    /^[\p{L}\p{N}_-]+$/u.test(decodeURIComponent(parts[1]))
  )
    return { url: "https://www.youtube.com/" + parts.join("/"), id: null };
  throw new Error(
    "Nhập link kênh /channel/UC…, /@handle, /c/… hoặc /user/…; không nhập link video.",
  );
}
export const validChannelId = (id) => CHANNEL_ID.test(id || "");
