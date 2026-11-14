class Attention extends Error {
  constructor(message) {
    super(message);
    this.attention = true;
  }
}
class BrowserDriver {
  constructor(tabId, origin, alive, progress) {
    this.tabId = tabId;
    this.origin = origin;
    this.alive = alive;
    this.progress = progress;
  }
  async guard() {
    if (!(await this.alive()))
      throw new Attention(
        "Công việc đã dừng hoặc phiên đã đổi. Giữ tab để kiểm tra kết quả.",
      );
    const tab = await chrome.tabs.get(this.tabId);
    let url;
    try {
      url = new URL(tab.url);
    } catch {}
    if (url?.hostname === "accounts.google.com")
      throw new Attention(
        "Chưa đăng nhập. Đăng nhập trong tab vừa mở rồi bấm Tiếp tục.",
      );
    if (url?.origin !== this.origin)
      throw new Attention(
        "Trang đã chuyển sang địa chỉ khác. Cần kiểm tra trong đúng profile.",
      );
    if (this.expectedPath && url.pathname !== this.expectedPath)
      throw new Attention(
        "Tab đã chuyển sang video khác. Mở lại đúng video của công việc rồi Tiếp tục.",
      );
  }
  async command(method, params = {}) {
    await this.guard();
    return chrome.debugger.sendCommand({ tabId: this.tabId }, method, params);
  }
  async page(fn, ...args) {
    const reply = await this.command("Runtime.evaluate", {
      expression: `(${fn.toString()})(...${JSON.stringify(args)})`,
      returnByValue: true,
      awaitPromise: true,
      userGesture: true,
    });
    if (reply.exceptionDetails)
      throw new Attention(
        "Không đọc/thao tác được giao diện: " +
          (reply.exceptionDetails.text || "trang đã thay đổi"),
      );
    return reply.result?.value;
  }
  async key(key, code, modifiers = 0) {
    await this.command("Input.dispatchKeyEvent", {
      type: "keyDown",
      key,
      code,
      windowsVirtualKeyCode: {
        Enter: 13,
        End: 35,
        F2: 113,
        Escape: 27,
        a: 65,
        c: 67,
      }[key],
      modifiers,
    });
    await this.command("Input.dispatchKeyEvent", {
      type: "keyUp",
      key,
      code,
      modifiers,
    });
  }
  async text(value) {
    await this.command("Input.insertText", { text: value });
  }
  async wait(read, accept, message, timeout = 20000) {
    const start = Date.now();
    while (Date.now() - start < timeout) {
      await this.guard();
      const value = await read();
      if (accept(value)) return value;
      await this.progress({ message });
      await new Promise((r) => setTimeout(r, 700));
    }
    throw new Attention(
      message + " Chưa xác minh được trong thời gian chờ; giữ tab để kiểm tra.",
    );
  }
  async attach() {
    await this.guard();
    try {
      await chrome.debugger.attach({ tabId: this.tabId }, "1.3");
    } catch {
      throw new Attention(
        "Chưa có quyền điều khiển tab. Kiểm tra quyền tiện ích hoặc đóng DevTools của tab này.",
      );
    }
  }
  async detach() {
    await chrome.debugger.detach({ tabId: this.tabId }).catch(() => {});
  }
  async click(labels) {
    const point = await this.page(pageButton, labels);
    if (!point)
      throw new Attention(
        "Không tìm thấy duy nhất nút " +
          labels.join(" / ") +
          ". Giao diện có thể đã thay đổi.",
      );
    await this.command("Input.dispatchMouseEvent", {
      type: "mousePressed",
      x: point.x,
      y: point.y,
      button: "left",
      clickCount: 1,
    });
    await this.command("Input.dispatchMouseEvent", {
      type: "mouseReleased",
      x: point.x,
      y: point.y,
      button: "left",
      clickCount: 1,
    });
  }
}
function pageButton(labels) {
  const visible = (el) =>
    el.getClientRects().length &&
    !el.disabled &&
    el.getAttribute("aria-disabled") !== "true";
  const roots = [document];
  for (let i = 0; i < roots.length; i++)
    for (const el of roots[i].querySelectorAll("*"))
      if (el.shadowRoot) roots.push(el.shadowRoot);
  const candidates = roots
    .flatMap((root) => [
      ...root.querySelectorAll(
        'button,[role="button"],ytcp-button,tp-yt-paper-item',
      ),
    ])
    .filter(
      (el) =>
        visible(el) &&
        labels.some((label) =>
          [
            el.getAttribute("aria-label"),
            el.getAttribute("title"),
            el.innerText?.trim(),
          ].includes(label),
        ),
    );
  const choices = candidates.filter(
    (el) => !candidates.some((other) => other !== el && el.contains(other)),
  );
  if (choices.length !== 1) return null;
  const box = choices[0].getBoundingClientRect();
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}
function pageLogin() {
  const text = document.body?.innerText || "";
  return {
    login:
      [
        ...document.querySelectorAll(
          'a[href*="accounts.google.com/ServiceLogin"]',
        ),
      ].some((el) => el.getClientRects().length) ||
      /^(Sign in|Đăng nhập)$/m.test(text),
    readOnly:
      /\bView only\b|Chỉ xem|You need access|Bạn cần có quyền truy cập/.test(
        text,
      ),
  };
}
globalThis.BrowserDriver = BrowserDriver;
globalThis.Attention = Attention;
