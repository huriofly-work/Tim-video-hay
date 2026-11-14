function parseSheetTsv(value) {
  const rows = [];
  let row = [],
    cell = "",
    quoted = false;
  for (let i = 0; i < value.length; i++) {
    const c = value[i];
    if (c === '"' && (quoted || cell === "")) {
      if (quoted && value[i + 1] === '"') {
        cell += '"';
        i++;
      } else quoted = !quoted;
    } else if (!quoted && (c === "\t" || c === "\n")) {
      row.push(cell.replace(/\r$/, ""));
      cell = "";
      if (c === "\n") {
        rows.push(row);
        row = [];
      }
    } else cell += c;
  }
  if (cell || row.length) {
    row.push(cell.replace(/\r$/, ""));
    rows.push(row);
  }
  return rows;
}
function sheetColumns(rows) {
  const headers = ["Link video mẫu", "Tên video", "Ngách", "Loại video"];
  for (let i = 0; i < Math.min(rows.length, 30); i++) {
    const values = rows[i].map((x) => x.trim());
    if (headers.every((h) => values.includes(h))) {
      if (headers.some((h) => values.filter((x) => x === h).length !== 1))
        throw new Error("Tiêu đề cột bị trùng.");
      return { header: i, columns: headers.map((h) => values.indexOf(h)) };
    }
  }
  throw new Error("Không tìm đủ bốn tiêu đề cột.");
}
function sheetLetter(i) {
  let value = "";
  for (i++; i; i = Math.floor((i - 1) / 26))
    value = String.fromCharCode(65 + ((i - 1) % 26)) + value;
  return value;
}
function sheetVideoId(value) {
  let text = String(value || "");
  if (/^=HYPERLINK\(/i.test(text))
    text = text.match(/^=HYPERLINK\("([^"]+)"/i)?.[1] || "";
  if (/^\d{15,22}$/.test(text)) return text;
  try {
    const u = new URL(text);
    if (
      u.protocol !== "https:" ||
      u.username ||
      u.password ||
      u.port ||
      !["douyin.com", "www.douyin.com"].includes(u.hostname)
    )
      return null;
    return (
      u.pathname.match(/^\/video\/(\d{15,22})\/?$/)?.[1] ||
      u.searchParams.get("modal_id")?.match(/^\d{15,22}$/)?.[0] ||
      null
    );
  } catch {
    return null;
  }
}
function sheetPlan(rows, id, pendingRow) {
  const { header, columns } = sheetColumns(rows);
  const match = rows.findIndex(
    (r, i) => i > header && sheetVideoId(r[columns[0]]) === id,
  );
  if (match >= 0 && match + 1 !== pendingRow)
    return { row: match + 1, columns, duplicate: true };
  if (pendingRow) {
    if (!Number.isInteger(pendingRow) || pendingRow <= header + 1)
      throw new Error("Checkpoint hàng không hợp lệ.");
    return { row: pendingRow, columns };
  }
  for (let i = rows.length - 1; i > header; i--)
    if (columns.every((c) => !rows[i]?.[c])) return { row: i + 1, columns };
  return { row: Math.max(rows.length, header + 1) + 1, columns };
}
globalThis.sheetPolicy = {
  parseSheetTsv,
  sheetColumns,
  sheetLetter,
  sheetVideoId,
  sheetPlan,
};
