function sheetNameBox() {
  const el = document.querySelector("#t-name-box");
  return el
    ? {
        value: el.value || el.innerText || "",
        focus: document.activeElement === el,
      }
    : null;
}
function sheetFormula() {
  const el = document.querySelector("#t-formula-bar-input");
  if (!el) return null;
  return el.value ?? el.innerText ?? null;
}
function sheetSaved() {
  const el = document.querySelector("#docs-save-indicator");
  const value =
    (el?.innerText || "") + " " + (el?.getAttribute("aria-label") || "");
  return /Saved to Drive|All changes saved|Đã lưu vào Drive|Đã lưu tất cả thay đổi/i.test(
    value,
  );
}
async function openSheet(driver, tabName) {
  const login = await driver.page(pageLogin);
  if (login.login)
    throw new Attention(
      "Google Sheets chưa đăng nhập. Đăng nhập trong đúng profile rồi bấm Tiếp tục.",
    );
  if (login.readOnly)
    throw new Attention("Tài khoản hiện tại chưa có quyền sửa bảng.");
  const matches = await driver.page((name) => {
    const nodes = [...document.querySelectorAll(".docs-sheet-tab-name")].filter(
      (el) => el.innerText?.trim() === name,
    );
    if (nodes.length !== 1) return false;
    nodes[0].click();
    return true;
  }, tabName);
  if (!matches)
    throw new Attention(
      "Không nhận diện được đúng tab " +
        tabName +
        ". Mở tab này trong bảng rồi thử lại.",
    );
  await driver.wait(
    () => driver.page(sheetNameBox),
    (x) => !!x,
    "Đang chờ giao diện ô Sheets.",
  );
  if ((await driver.page(sheetFormula)) === null)
    throw new Attention(
      "Không đọc được thanh công thức; dừng để bảo vệ ô có công thức.",
    );
}
async function sheetSelect(driver, range) {
  if (!/^[A-Z]+\d+(?::[A-Z]+\d+)?$/.test(range))
    throw new Error("Vùng ô không hợp lệ.");
  await driver.page(() => document.querySelector("#t-name-box")?.focus());
  if (!(await driver.page(sheetNameBox))?.focus)
    throw new Attention("Không đặt được focus vào ô tên Sheets.");
  await driver.key("a", "KeyA", 2);
  await driver.text(range);
  await driver.key("Enter", "Enter");
  await driver.wait(
    () => driver.page(sheetNameBox),
    (x) => x?.value?.replace(/\s/g, "") === range,
    "Đang chọn vùng " + range + ".",
  );
}
async function sheetCopy(driver, range) {
  await sheetSelect(driver, range);
  const sentinel = "video-sheet-copy-" + crypto.randomUUID();
  await chrome.scripting.executeScript({
    target: { tabId: driver.tabId },
    func: async (value) => navigator.clipboard.writeText(value),
    args: [sentinel],
  });
  await driver.key("c", "KeyC", 2);
  return driver.wait(
    async () => {
      const [{ result }] = await chrome.scripting.executeScript({
        target: { tabId: driver.tabId },
        func: async () => navigator.clipboard.readText(),
      });
      return result;
    },
    (x) => typeof x === "string" && x !== sentinel,
    "Đang đọc vùng " + range + ".",
  );
}
async function sheetCell(driver, address) {
  await sheetSelect(driver, address);
  return driver.page(sheetFormula);
}
async function writeSheet(driver, job, progress) {
  await openSheet(driver, job.sheetTab);
  await sheetSelect(driver, "A1");
  await driver.key("End", "End", 2);
  const edge = await driver.page(sheetNameBox);
  const lastRow = Number(edge?.value?.match(/\d+$/)?.[0]);
  if (!lastRow || lastRow > 100000)
    throw new Attention("Chưa đọc được phạm vi hàng hợp lệ của bảng.");
  const rows = parseSheetTsv(
    await sheetCopy(driver, "A1:CV" + Math.max(lastRow, 30)),
  );
  const info = sheetColumns(rows);
  for (let i = info.header + 1; i < rows.length; i++) {
    const raw = rows[i]?.[info.columns[0]];
    if (raw && !sheetVideoId(raw))
      rows[i][info.columns[0]] = await sheetCell(
        driver,
        sheetLetter(info.columns[0]) + (i + 1),
      );
  }
  const planned = sheetPlan(rows, job.videoId, job.pendingRow);
  if (planned.duplicate) {
    const existing = [];
    for (const column of planned.columns)
      existing.push(await sheetCell(driver, sheetLetter(column) + planned.row));
    if (
      sheetVideoId(existing[0]) !== job.videoId ||
      !existing.every((value) => value && value !== '=""')
    )
      throw new Attention(
        "Hàng video có dữ liệu thay đổi hoặc còn ô trống. Kiểm tra hàng " +
          planned.row +
          " trước khi tiếp tục.",
      );
    await driver.wait(
      () => driver.page(sheetSaved),
      (x) => x,
      "Đang xác minh bảng đã lưu.",
    );
    return { ok: true, row: planned.row, duplicate: true };
  }
  await progress({
    message: "Chuẩn bị ghi hàng " + planned.row,
    sheetRow: planned.row,
  });
  const knownTypes = rows
    .slice(info.header + 1)
    .map((row) => row[info.columns[3]]);
  const localized =
    knownTypes.some((value) => ["Video ngắn", "Video dài"].includes(value)) &&
    !knownTypes.some((value) => ["Shorts", "Long"].includes(value));
  const expected = [
    job.url,
    job.title,
    job.niche,
    localized ? (job.type === "Shorts" ? "Video ngắn" : "Video dài") : job.type,
  ];
  const addresses = planned.columns.map((c) => sheetLetter(c) + planned.row);
  const readCells = async () => {
    const values = [];
    for (const address of addresses)
      values.push(await sheetCell(driver, address));
    return values;
  };
  const previous = await readCells();
  const safe = (value, i) =>
    value === "" || (job.pendingRow === planned.row && value === expected[i]);
  if (previous.some((v, i) => v === null || !safe(v, i)))
    throw new Attention(
      "Hàng " +
        planned.row +
        " có dữ liệu hoặc công thức. Dừng để tránh ghi đè.",
    );
  for (let i = 0; i < 4; i++) {
    // Only an unfilled type cell may adopt the actual dropdown's localized label.
    if (i === 3 && !previous[i]) {
      const options = await driver.page(() =>
        [...document.querySelectorAll('[role="option"]')]
          .map((el) => el.innerText?.trim())
          .filter(Boolean),
      );
      if (options?.includes(job.type === "Shorts" ? "Video ngắn" : "Video dài"))
        expected[i] = job.type === "Shorts" ? "Video ngắn" : "Video dài";
    }
    const current = await sheetCell(driver, addresses[i]);
    if (current === expected[i]) continue;
    if (current !== "")
      throw new Attention(
        "Ô " + addresses[i] + " vừa thay đổi. Giữ dữ liệu và dừng ghi.",
      );
    await progress({
      message: "Đang ghi " + addresses[i],
      sheetRow: planned.row,
      sheetColumn: planned.columns[i],
    });
    await driver.text(expected[i]);
    await driver.key("Enter", "Enter");
    await driver.wait(
      () => sheetCell(driver, addresses[i]),
      (x) => x === expected[i],
      "Đang xác minh " + addresses[i] + ".",
    );
  }
  const after = await readCells();
  if (after.some((value, i) => value !== expected[i]))
    throw new Attention(
      "Bốn ô vừa thay đổi hoặc chưa khớp. Kiểm tra hàng " + planned.row + ".",
    );
  await driver.wait(
    () => driver.page(sheetSaved),
    (x) => x,
    "Đang chờ Google Sheets lưu vào Drive.",
  );
  return { ok: true, row: planned.row, values: expected };
}
