export function sheetLink(value) {
  let url;
  try {
    url = new URL(String(value || "").trim());
  } catch {
    throw new Error("Nhập link Google Sheets đầy đủ của bảng muốn dùng.");
  }
  const id = url.pathname.match(
    /^\/spreadsheets\/(?:u\/\d+\/)?d\/([\w-]{20,200})(?:\/(?:edit|view|preview|copy|htmlview))?\/?$/,
  )?.[1];
  if (
    url.protocol !== "https:" ||
    url.hostname !== "docs.google.com" ||
    url.username ||
    url.password ||
    url.port ||
    !id
  )
    throw new Error(
      "Link cần là bảng Google Sheets tại https://docs.google.com/spreadsheets/d/...; không dùng link xuất bản hoặc loại tài liệu khác.",
    );
  return { id, url: "https://docs.google.com/spreadsheets/d/" + id + "/edit" };
}
export function sheetTabName(value) {
  const name = String(value || "")
    .normalize("NFC")
    .trim();
  if (!name || [...name].length > 100 || /[\u0000-\u001f\u007f]/.test(name))
    throw new Error(
      "Nhập đúng tên tab trong bảng, từ 1 đến 100 ký tự và không có xuống dòng.",
    );
  return name;
}
