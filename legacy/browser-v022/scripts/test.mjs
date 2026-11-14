import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
const root = fileURLToPath(new URL("../", import.meta.url)),
  temp = path.join(root, "test-output", "tmp");
fs.mkdirSync(temp, { recursive: true });
const files = fs
  .readdirSync(path.join(root, "test", "v2"))
  .filter((name) => name.endsWith(".test.mjs"))
  .map((name) => path.join("test", "v2", name));
const result = spawnSync(
  process.execPath,
  ["--test", "--test-reporter=spec", ...files],
  {
    cwd: root,
    env: { ...process.env, TEMP: temp, TMP: temp, TMPDIR: temp },
    stdio: "inherit",
  },
);
if (result.error) console.error(result.error.message);
process.exitCode = result.status ?? 1;
