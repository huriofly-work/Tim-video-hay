import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { normalizeVideo } from "../../src/domain.mjs";
import { downloadVideo } from "../../src/media.mjs";
const id = "7663523864474930442";
const info = (width, height) => ({
  streams: [
    { codec_type: "video", width, height, codec_name: "hevc" },
    { codec_type: "audio" },
  ],
  format: { duration: "300" },
});
test("additional addresses are kept only when supplied; a download address is not labelled original", () => {
  const v = normalizeVideo({
    aweme_id: id,
    video: {
      play_addr_h265: { url_list: ["https://hevc"], width: 2560, height: 1440 },
      download_addr: { url_list: ["https://provided-download"] },
    },
  });
  assert.equal(v.media.length, 2);
  assert.equal(v.media[0].codec, "hevc");
  assert.equal(v.media[1].sourceKind, "download");
  assert.equal(v.media[1].width, null);
  assert.equal(v.media[1].originalVerified, undefined);
});
test("an unlabelled source is probed and can outrank all known renditions", async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "additional-quality-"));
  const attempted = [];
  try {
    const result = await downloadVideo(
      {
        duration: 300,
        media: [
          { url: "2k", width: 2560, height: 1440 },
          { url: "provided", sourceField: "download_addr" },
        ],
      },
      path.join(root, "v.mp4"),
      null,
      {
        probeSource: async (m) => {
          assert.equal(m.url, "provided");
          return info(3840, 2160);
        },
        download: async (url, file) => {
          attempted.push(url);
          await fs.writeFile(file, "v");
        },
        probe: async () => info(3840, 2160),
      },
    );
    assert.deepEqual(attempted, ["provided"]);
    assert.equal(result.height, 2160);
    assert.equal(result.originalVerified, false);
  } finally {
    await fs.rm(root, { recursive: true, force: true });
  }
});
test("a failed remote probe still allows file verification before declaring a rendition best", async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "unknown-quality-"));
  try {
    const attempted = [];
    const result = await downloadVideo(
      {
        duration: 300,
        media: [
          { url: "2k", width: 2560, height: 1440 },
          { url: "unlabelled" },
        ],
      },
      path.join(root, "v.mp4"),
      null,
      {
        probeSource: async () => {
          throw new Error("Remote probe unavailable");
        },
        download: async (url, file) => {
          attempted.push(url);
          await fs.writeFile(file, url);
        },
        probe: async (file) =>
          info(
            ...((await fs.readFile(file, "utf8")) === "unlabelled"
              ? [3840, 2160]
              : [2560, 1440]),
          ),
      },
    );
    assert.deepEqual(attempted, ["unlabelled"]);
    assert.equal(result.height, 2160);
  } finally {
    await fs.rm(root, { recursive: true, force: true });
  }
});
test("an unknown candidate does not stop checking a higher bitrate source with the same dimensions and codec", async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "same-size-quality-"));
  try {
    const attempted = [];
    const result = await downloadVideo(
      {
        duration: 300,
        media: [
          {
            url: "known",
            width: 3840,
            height: 2160,
            codec: "hevc",
            bitrate: 3000000,
          },
          { url: "unknown", bitrate: 1000000 },
        ],
      },
      path.join(root, "v.mp4"),
      null,
      {
        probeSource: async () => {
          throw new Error("No metadata");
        },
        download: async (url, file) => {
          attempted.push(url);
          await fs.writeFile(file, url);
        },
        probe: async () => info(3840, 2160),
      },
    );
    assert.deepEqual(attempted, ["unknown", "known"]);
    assert.equal(result.bitrate, 3000000);
    assert.equal(await fs.readFile(path.join(root, "v.mp4"), "utf8"), "known");
  } finally {
    await fs.rm(root, { recursive: true, force: true });
  }
});
test("2K sources outrank 1080p with a higher bitrate; conflicting default dimensions do not replace a variant", () => {
  const v = normalizeVideo({
    aweme_id: id,
    video: {
      width: 2560,
      height: 1440,
      play_addr: { url_list: ["https://example/1080"] },
      bit_rate: [
        {
          bit_rate: 1500000,
          play_addr: {
            width: 1920,
            height: 1080,
            url_list: ["https://example/1080"],
          },
        },
        {
          bit_rate: 1100000,
          is_h265: 1,
          play_addr: {
            width: 2560,
            height: 1440,
            url_list: ["https://example/2k"],
          },
        },
      ],
    },
  });
  assert.equal(v.media.length, 2);
  assert.equal(v.media[0].height, 1440);
  assert.equal(v.media[1].height, 1080);
});
test("download verifies actual dimensions and tries another 2K source when a label is wrong", async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-quality-"));
  const file = path.join(dir, "video.mp4");
  const attempts = [];
  try {
    const result = await downloadVideo(
      {
        duration: 300,
        media: [
          { url: "bad-label", width: 2560, height: 1440, bitrate: 1200000 },
          { url: "real-2k", width: 2560, height: 1440, bitrate: 1100000 },
          { url: "1080", width: 1920, height: 1080, bitrate: 1500000 },
        ],
      },
      file,
      null,
      {
        download: async (url, output) => {
          attempts.push(url);
          await fs.writeFile(output, url);
        },
        probe: async (output) =>
          info(
            ...((await fs.readFile(output, "utf8")) === "real-2k"
              ? [2560, 1440]
              : [1920, 1080]),
          ),
      },
    );
    assert.deepEqual(attempts, ["bad-label", "real-2k"]);
    assert.equal(result.height, 1440);
    assert.equal(await fs.readFile(file, "utf8"), "real-2k");
    assert.deepEqual(await fs.readdir(dir), ["video.mp4"]);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
test("unavailable high resolution falls back with an explicit quality warning", async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), "video-fallback-"));
  try {
    const result = await downloadVideo(
      {
        duration: 300,
        media: [
          { url: "2k", width: 2560, height: 1440 },
          { url: "1080", width: 1920, height: 1080 },
        ],
      },
      path.join(dir, "v.mp4"),
      null,
      {
        download: async (url, output) => {
          if (url === "2k") throw new Error("Expired");
          await fs.writeFile(output, "ok");
        },
        probe: async () => info(1920, 1080),
      },
    );
    assert.match(result.warning, /1920×1080/);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
