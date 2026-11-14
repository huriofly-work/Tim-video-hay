function channelPageId() {
  const values = [
    document.querySelector('meta[itemprop="channelId"]')?.content,
    document
      .querySelector('link[rel="canonical"]')
      ?.href?.match(/\/channel\/(UC[\w-]{22})/)?.[1],
    document
      .querySelector('meta[property="og:url"]')
      ?.content?.match(/\/channel\/(UC[\w-]{22})/)?.[1],
  ];
  return values.find((x) => /^UC[\w-]{22}$/.test(x || "")) || null;
}
function studioIdentity(id) {
  const text = document.body?.innerText || "";
  const name = document
    .querySelector("#entity-name,#channel-name,.channel-name")
    ?.innerText?.trim();
  const links = [...document.querySelectorAll("a[href]")]
    .map((a) => a.href)
    .filter((h) => h.startsWith("https://studio.youtube.com/channel/"));
  return {
    login: /^(Sign in|Đăng nhập)$/m.test(text),
    denied:
      /You don.t have access|You do not have access|Bạn không có quyền|Không có quyền truy cập/.test(
        text,
      ),
    ok:
      !!name &&
      !!document.querySelector("ytcp-app") &&
      links.some((h) => h.split("/")[4] === id),
    name,
  };
}
function studioUploadState() {
  const dialog = document.querySelector("ytcp-video-upload-dialog");
  if (!dialog) return { dialog: false };
  const text = dialog.innerText || "";
  const link = [...dialog.querySelectorAll("a[href]")]
    .map((a) => a.href)
    .find((x) =>
      /youtu\.be\/[\w-]{11}|youtube\.com\/watch\?v=[\w-]{11}/.test(x),
    );
  const id = link?.match(/(?:youtu\.be\/|[?&]v=)([\w-]{11})/)?.[1];
  const selected = (name) => {
    const el = dialog.querySelector('[name="' + name + '"]');
    return (
      !!el &&
      (el.checked ||
        el.hasAttribute("checked") ||
        el.getAttribute("aria-checked") === "true")
    );
  };
  const notKids = selected("VIDEO_MADE_FOR_KIDS_NOT_MFK");
  return {
    dialog: true,
    id,
    text: text.slice(-15000),
    notKids,
    hasAudience: !!dialog.querySelector('[name="VIDEO_MADE_FOR_KIDS_NOT_MFK"]'),
    unlisted: selected("UNLISTED"),
    visibility: !!dialog.querySelector('[name="UNLISTED"]'),
    uploaded:
      /Upload complete|Upload completed|Đã tải lên|Tải lên hoàn tất|Processing complete|Đã xử lý xong/i.test(
        text,
      ),
    error:
      /Title is required|Tiêu đề là bắt buộc|Daily upload limit|Giới hạn tải lên hằng ngày/i.test(
        text,
      ),
  };
}
function studioFileInput(ready = false) {
  const inputs = [
    ...document.querySelectorAll(
      'ytcp-uploads-dialog input[type="file"],ytcp-video-upload-dialog input[type="file"]',
    ),
  ].filter(
    (el) =>
      el
        .closest("ytcp-uploads-dialog,ytcp-video-upload-dialog")
        ?.getClientRects().length,
  );
  return ready ? inputs.length === 1 : inputs.length === 1 ? inputs[0] : null;
}
async function verifyStudio(driver, id) {
  const state = await driver.wait(
    () => driver.page(studioIdentity, id),
    (s) => s.ok || s.login || s.denied,
    "Đang kiểm tra kênh trong Studio.",
  );
  if (state.login)
    throw new Attention(
      "YouTube Studio chưa đăng nhập. Đăng nhập trong đúng profile rồi bấm Tiếp tục.",
    );
  if (state.denied || !state.ok)
    throw new Attention("Tài khoản chưa có quyền đăng lên kênh đã liên kết.");
  return state;
}
async function selectStudioRadio(driver, name) {
  const done = await driver.page((value) => {
    const matches = [
      ...document.querySelectorAll('[name="' + value + '"]'),
    ].filter((el) => el.getClientRects().length);
    if (matches.length !== 1) return false;
    const el = matches[0];
    el.click();
    return true;
  }, name);
  if (!done)
    throw new Attention(
      "Không nhận diện được lựa chọn " + name + " trong Studio.",
    );
}
async function studioConfirm(driver, job, progress) {
  driver.expectedPath = "/video/" + job.youtubeId + "/edit";
  await verifyStudio(driver, job.channelId);
  const result = await driver.wait(
    () =>
      driver.page(() => {
        const el = document.querySelector("#visibility");
        const text =
          (el?.innerText || "") + " " + (el?.getAttribute("aria-label") || "");
        const radio = document.querySelector(
          '[name="VIDEO_MADE_FOR_KIDS_NOT_MFK"]',
        );
        return {
          unlisted: /\bUnlisted\b|Không công khai/.test(text),
          private: /\bPrivate\b|Riêng tư/.test(text),
          notKids:
            !!radio &&
            (radio.checked ||
              radio.hasAttribute("checked") ||
              radio.getAttribute("aria-checked") === "true"),
          hasAudience: !!radio,
        };
      }),
    (x) => x.private || (x.unlisted && x.hasAudience),
    "Đang kiểm tra chế độ video đã tạo.",
  );
  if (!result.unlisted)
    throw new Attention(
      "Video đã có trên YouTube nhưng chưa ở chế độ không công khai. Chọn không công khai trong tab này rồi Tiếp tục; không upload lại.",
    );
  if (!result.notKids)
    throw new Attention(
      "Chưa xác minh video không dành cho trẻ em. Kiểm tra lựa chọn trong tab Studio, lưu rồi Tiếp tục; không upload lại.",
    );
  await progress({
    message: "Đã xác minh video không công khai.",
    youtubeId: job.youtubeId,
  });
  return {
    id: job.youtubeId,
    channelId: job.channelId,
    visibility: "unlisted",
    audience: "not-kids",
  };
}
async function uploadStudio(driver, job, progress) {
  await verifyStudio(driver, job.channelId);
  await driver.click(["Create", "Tạo"]);
  await driver.click(["Upload videos", "Tải video lên"]);
  await driver.wait(
    () => driver.page(studioFileInput, true),
    (x) => x,
    "Đang mở hộp tải video.",
  );
  const ref = await driver.command("Runtime.evaluate", {
    expression: "(" + studioFileInput.toString() + ")()",
    returnByValue: false,
  });
  if (!ref.result?.objectId)
    throw new Attention("Không nhận diện được ô chọn file video.");
  // Mark the potentially consequential phase before sending the file, including lost responses.
  await verifyStudio(driver, job.channelId);
  await progress({
    message: "Đang gửi file video vào YouTube Studio.",
    uploadStarted: true,
  });
  await driver.command("DOM.setFileInputFiles", {
    files: [job.file],
    objectId: ref.result.objectId,
  });
  let reported;
  for (let step = 0; step < 6; step++) {
    const state = await driver.wait(
      () => driver.page(studioUploadState),
      (x) => x.dialog && (step !== 0 || x.hasAudience),
      "Đang chờ giao diện thông tin video.",
    );
    if (state.id) {
      reported = state.id;
      await progress({
        youtubeId: reported,
        message: "YouTube đã tạo bản nháp; đang hoàn tất cài đặt.",
      });
    }
    if (state.error)
      throw new Attention(
        "Studio yêu cầu thông tin bắt buộc hoặc đã chạm giới hạn. Xử lý trong tab rồi Tiếp tục.",
      );
    if (state.hasAudience && !state.notKids && !state.visibility) {
      await selectStudioRadio(driver, "VIDEO_MADE_FOR_KIDS_NOT_MFK");
      await driver.wait(
        () => driver.page(studioUploadState),
        (x) => x.notKids,
        "Đang xác minh video không dành cho trẻ em.",
      );
    }
    if (state.visibility) {
      await selectStudioRadio(driver, "UNLISTED");
      await driver.wait(
        () => driver.page(studioUploadState),
        (x) => x.unlisted,
        "Đang xác minh chế độ không công khai.",
      );
      await verifyStudio(driver, job.channelId);
      await driver.wait(
        () => driver.page(studioUploadState),
        (x) => x.uploaded,
        "Đang chờ tải file hoàn tất.",
        3600000,
      );
      const before = await driver.page(studioUploadState);
      if (!before.unlisted || (before.hasAudience && !before.notKids))
        throw new Attention(
          "Chế độ hiển thị hoặc đối tượng người xem vừa thay đổi. Dừng trước khi lưu.",
        );
      if (before.id) {
        reported = before.id;
        await progress({
          youtubeId: reported,
          message: "Đang lưu video không công khai.",
        });
      }
      await driver.click(["Save", "Lưu"]);
      await driver.wait(
        () => driver.page(studioUploadState),
        (x) =>
          !x.dialog ||
          /Video published|Video saved|Đã đăng video|Đã lưu video/i.test(
            x.text,
          ),
        "Đang chờ Studio xác nhận đã lưu.",
      );
      if (!reported)
        throw new Attention(
          "Chưa lấy được link video từ Studio. Kiểm tra kết quả trước khi upload lại.",
        );
      return {
        id: reported,
        channelId: job.channelId,
        visibility: "pending",
        needsVerification: true,
      };
    }
    await driver.wait(
      () => driver.page(pageButton, ["Next", "Tiếp"]),
      (x) => !!x,
      "Đang chờ bước tiếp theo của Studio.",
      60000,
    );
    await driver.click(["Next", "Tiếp"]);
    // Advance only after the old step has changed, so a slow render never receives duplicate clicks.
    await driver.wait(
      () => driver.page(studioUploadState),
      (x) => x.text !== state.text,
      "Đang chuyển bước upload.",
    );
  }
  throw new Attention(
    "Studio có thêm bước bắt buộc; cần xử lý trong tab đang mở.",
  );
}
