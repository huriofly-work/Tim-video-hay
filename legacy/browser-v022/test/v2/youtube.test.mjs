import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import vm from "node:vm";
async function load(context = {}) {
  for (const file of ["browser-driver.js", "youtube-studio.js"])
    vm.runInNewContext(
      await fs.readFile(
        new URL("../../extension/" + file, import.meta.url),
        "utf8",
      ),
      context,
    );
  return context;
}
const channelId = "UC" + "a".repeat(22);
const wait = async (read, accept) => {
  const value = await read();
  assert.ok(accept(value), "mock state must satisfy the awaited condition");
  return value;
};
test("Studio only selects a unique file input inside an open upload dialog", async () => {
  const visible = { closest: () => ({ getClientRects: () => [1] }) },
    hidden = { closest: () => ({ getClientRects: () => [] }) };
  let inputs = [visible, hidden];
  const context = await load({ document: { querySelectorAll: () => inputs } });
  assert.equal(context.studioFileInput(), visible);
  inputs = [visible, visible];
  assert.equal(context.studioFileInput(), null);
  inputs = [hidden];
  assert.equal(context.studioFileInput(true), false);
});
test("channel access denial prevents all upload clicks and file transfer", async () => {
  const context = await load();
  let mutations = 0;
  const driver = {
    wait,
    page: async () => ({ denied: true }),
    click: async () => {
      mutations++;
    },
    command: async () => {
      mutations++;
    },
  };
  await assert.rejects(
    context.uploadStudio(driver, { channelId }, async () => {}),
    /quyền đăng/,
  );
  assert.equal(mutations, 0);
});
for (const initiallyNotKids of [true, false])
  test(
    "Studio upload verifies unlisted and not-kids, including a conflicting channel default: " +
      initiallyNotKids,
    async () => {
      const context = await load();
      let stage = 0,
        unlisted = false,
        marked = false,
        fileSent = false,
        saved = false,
        notKids = initiallyNotKids;
      const clicks = [],
        commands = [];
      const driver = {
        wait,
        page: async (fn, ...args) => {
          if (fn.name === "studioIdentity")
            return { ok: true, name: "Configured channel" };
          if (fn.name === "studioFileInput") return true;
          if (fn.name === "pageButton") return { x: 1, y: 1 };
          if (fn.name === "studioUploadState")
            return {
              dialog: !saved,
              text: "step " + stage,
              hasAudience: true,
              notKids,
              visibility: stage === 3,
              unlisted,
              uploaded: true,
              id: "abcdefghijk",
            };
          if (args[0] === "VIDEO_MADE_FOR_KIDS_NOT_MFK") notKids = true;
          else if (args[0] === "UNLISTED") unlisted = true;
          else assert.fail("Unexpected radio choice");
          return true;
        },
        command: async (method, params) => {
          commands.push(method);
          if (method === "Runtime.evaluate")
            return { result: { objectId: "input" } };
          if (method === "DOM.setFileInputFiles") {
            assert.equal(marked, true);
            assert.deepEqual(Array.from(params.files), ["F:\\Videos\\Tên.mp4"]);
            fileSent = true;
          }
        },
        click: async (labels) => {
          clicks.push(labels[0]);
          if (labels[0] === "Next") stage++;
          if (labels[0] === "Save") {
            assert.equal(unlisted, true);
            assert.equal(notKids, true);
            assert.equal(fileSent, true);
            saved = true;
          }
        },
        text: async () => {
          assert.fail(
            "Upload must leave the default title and optional fields untouched",
          );
        },
      };
      const result = await context.uploadStudio(
        driver,
        { channelId, file: "F:\\Videos\\Tên.mp4", audience: "kids" },
        async (value) => {
          if (value.uploadStarted) marked = true;
        },
      );
      assert.equal(result.id, "abcdefghijk");
      assert.equal(result.needsVerification, true);
      assert.equal(result.visibility, "pending");
      assert.equal(clicks.at(-1), "Save");
      assert.deepEqual(commands, ["Runtime.evaluate", "DOM.setFileInputFiles"]);
    },
  );
test("confirmation treats private as incomplete and keeps the existing video instead of uploading again", async () => {
  const context = await load();
  const driver = {
    wait,
    page: async (fn) =>
      fn.name === "studioIdentity"
        ? { ok: true, name: "Channel" }
        : { private: true, unlisted: false },
    command: async () => assert.fail("No file transfer while confirming"),
  };
  await assert.rejects(
    context.studioConfirm(
      driver,
      { channelId, youtubeId: "abcdefghijk" },
      async () => {},
    ),
    /không upload lại/,
  );
});
for (const notKids of [true, false])
  test(
    "confirmation requires not-kids on the saved video: " + notKids,
    async () => {
      const context = await load();
      const driver = {
        wait,
        page: async (fn) =>
          fn.name === "studioIdentity"
            ? { ok: true, name: "Channel" }
            : { unlisted: true, hasAudience: true, notKids },
        command: async () =>
          assert.fail("Confirmation must not resend the file"),
      };
      const confirmation = context.studioConfirm(
        driver,
        { channelId, youtubeId: "abcdefghijk" },
        async () => {},
      );
      if (notKids) {
        const result = await confirmation;
        assert.equal(result.audience, "not-kids");
        assert.equal(result.visibility, "unlisted");
      } else await assert.rejects(confirmation, /không dành cho trẻ em/);
    },
  );
