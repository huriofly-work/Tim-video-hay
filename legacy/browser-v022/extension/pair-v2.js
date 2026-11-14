if (location.origin === "http://127.0.0.1:43127") {
  let attaching = false;
  window.addEventListener("message", async (event) => {
    if (
      event.source !== window ||
      event.origin !== location.origin ||
      event.data?.source !== "video-app-v2" ||
      !/^[\w-]{20,80}$/.test(event.data.sessionId || "")
    )
      return;
    if (event.data.type === "wake") {
      chrome.runtime
        .sendMessage({ type: "wake", sessionId: event.data.sessionId })
        .catch(() => {});
      return;
    }
    if (event.data.type !== "attach" || attaching) return;
    attaching = true;
    try {
      const response = await fetch("/api/pair", {
        credentials: "same-origin",
        cache: "no-store",
      });
      if (!response.ok) throw new Error("Mở lại ứng dụng để tạo phiên.");
      const { token } = await response.json();
      const result = await chrome.runtime.sendMessage({
        type: "attach-app",
        sessionId: event.data.sessionId,
        token,
      });
      window.postMessage(
        {
          source: "video-extension-v2",
          sessionId: event.data.sessionId,
          ...result,
        },
        location.origin,
      );
    } catch (error) {
      window.postMessage(
        {
          source: "video-extension-v2",
          sessionId: event.data.sessionId,
          ok: false,
          message: error.message,
        },
        location.origin,
      );
    } finally {
      attaching = false;
    }
  });
}
