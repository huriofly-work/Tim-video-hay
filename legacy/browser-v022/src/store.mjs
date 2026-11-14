import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
export class Store {
  constructor(dir) {
    this.dir = dir;
    fs.mkdirSync(dir, { recursive: true });
    this.file = path.join(dir, "state.json");
    const previous = fs.existsSync(this.file)
      ? JSON.parse(fs.readFileSync(this.file, "utf8"))
      : null;
    if (previous && previous.version !== 2) {
      const backup = path.join(
        dir,
        "state-before-v2-" + crypto.randomUUID() + ".json",
      );
      fs.copyFileSync(this.file, backup, fs.constants.COPYFILE_EXCL);
      this.legacyBackup = backup;
    }
    this.state =
      previous?.version === 2
        ? previous
        : {
            version: 2,
            bridgeToken: crypto.randomBytes(32).toString("hex"),
            settings: {
              sheetId:
                previous?.settings?.sheetId ||
                "1eFhhJNvsZnN1k1MOssdikZ8PVYC177Gbz0sGdczyyRA",
              sheetTab: previous?.settings?.sheetTab || "Bảng công việc",
              outputDir:
                previous?.settings?.outputDir ||
                previous?.settings?.downloadOutputDir ||
                "",
              channelUrl: "",
              audience: "not-kids",
            },
            jobs: {},
            currentJobId: null,
            legacyBackup: this.legacyBackup || null,
          };
    this.state.settings.audience = "not-kids";
    this.save();
  }
  save() {
    const temp = this.file + ".tmp";
    fs.writeFileSync(temp, JSON.stringify(this.state, null, 2));
    fs.renameSync(temp, this.file);
  }
}
