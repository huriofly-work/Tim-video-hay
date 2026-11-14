(() => {
  const videos = new Map();
  window.addEventListener("message", (event) => {
    if (
      event.source !== window ||
      event.origin !== location.origin ||
      event.data?.source !== "tim-video-hay" ||
      !Array.isArray(event.data.items)
    )
      return;
    for (const item of event.data.items) {
      if (typeof item.aweme_id === "string") {
        videos.set(item.aweme_id, item);
        if (videos.size > 100) videos.delete(videos.keys().next().value);
      }
    }
  });
  chrome.runtime.onMessage.addListener((message, sender, reply) => {
    if (message.type === "highest-quality") {
      const control = document.querySelector(".xgplayer-playclarity-setting");
      control?.click();
      setTimeout(() => {
        const options = [
          ...(control?.querySelectorAll(".virtual .item") || []),
        ];
        const weight = (text) =>
          /4K|原画/.test(text)
            ? 4
            : /2K/.test(text)
              ? 3
              : /1080/.test(text)
                ? 2
                : /720/.test(text)
                  ? 1
                  : 0;
        options.sort((a, b) => weight(b.innerText) - weight(a.innerText));
        options[0]?.click();
        reply({ ok: !!options[0] });
      }, 300);
      return true;
    }
    if (message.type !== "read-video") return;
    if (location.pathname !== "/video/" + message.videoId) {
      reply({
        attention: true,
        message:
          "Tab Douyin đã chuyển sang video khác. Mở lại đúng link rồi Tiếp tục.",
      });
      return;
    }
    const text = document.body?.innerText || "";
    if (/完成验证|安全验证|请完成验证/.test(text)) {
      reply({
        attention: true,
        message: "Douyin yêu cầu xác minh. Hoàn tất trong tab rồi Tiếp tục.",
      });
      return;
    }
    if (/登录后观看|请先登录/.test(text)) {
      reply({
        attention: true,
        message:
          "Douyin yêu cầu đăng nhập. Đăng nhập trong profile này rồi Tiếp tục.",
      });
      return;
    }
    const observed = videos.get(message.videoId),
      player = document.querySelector("video");
    const media =
      player?.currentSrc && /^https:/.test(player.currentSrc)
        ? [
            {
              url: player.currentSrc,
              width: player.videoWidth,
              height: player.videoHeight,
              dimensionsSource: "player",
            },
          ]
        : [];
    if (observed?.video?.play_addr || observed?.video?.bit_rate?.length) {
      reply({
        video: {
          aweme_id: message.videoId,
          video: observed.video,
          duration: Number.isFinite(player?.duration)
            ? player.duration
            : undefined,
          media,
        },
      });
      return;
    }
    if (
      media.length &&
      Number.isFinite(player.duration) &&
      player.duration > 0
    ) {
      reply({
        video: { id: message.videoId, duration: player.duration, media },
      });
      return;
    }
    window.postMessage({ source: "tim-video-hay-request" }, location.origin);
    reply({ waiting: true });
  });
})();
