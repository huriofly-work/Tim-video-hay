import fs from "node:fs/promises";
import path from "node:path";
import crypto from "node:crypto";
export async function listFolders(value = "") {
  if (!value) {
    const results = await Promise.all(
      Array.from(
        { length: 26 },
        (_, i) => String.fromCharCode(65 + i) + ":\\",
      ).map(async (p) => {
        try {
          if ((await fs.stat(p)).isDirectory()) return { name: p, path: p };
        } catch {}
        return null;
      }),
    );
    return { path: "", parent: null, folders: results.filter(Boolean) };
  }
  if (!path.isAbsolute(value)) throw new Error("Cần đường dẫn đầy đủ.");
  const current = path.resolve(value),
    entries = await fs.readdir(current, { withFileTypes: true });
  return {
    path: current,
    parent: path.dirname(current) === current ? "" : path.dirname(current),
    folders: entries
      .filter((e) => e.isDirectory() && !e.name.startsWith("."))
      .sort((a, b) => a.name.localeCompare(b.name))
      .map((e) => ({ name: e.name, path: path.join(current, e.name) })),
  };
}
export async function checkOutput(value) {
  if (!value || !path.isAbsolute(value))
    throw new Error(
      "Chưa chọn thư mục tổng. Chọn thư mục hoặc nhập đường dẫn đầy đủ rồi lưu.",
    );
  const dir = path.resolve(value);
  if (!(await fs.stat(dir)).isDirectory())
    throw new Error("Đường dẫn không phải thư mục.");
  const probe = path.join(dir, ".video-write-check-" + crypto.randomUUID());
  await fs.writeFile(probe, "", { flag: "wx" });
  await fs.unlink(probe);
  return dir;
}
