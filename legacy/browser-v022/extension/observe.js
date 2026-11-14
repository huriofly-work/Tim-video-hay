// Observe responses already requested by the page. No private API signing or cookie export.
(() => {
  const keep = new Map();
  function inspect(root) {
    const target = location.pathname.match(/^\/video\/(\d{15,22})\/?$/)?.[1];
    if (!target) return;
    let visited = 0;
    function walk(x, depth) {
      if (!x || typeof x !== "object" || depth > 14 || ++visited > 25000)
        return;
      if (x.aweme_id === target && x.video) {
        const item = {
          aweme_id: x.aweme_id,
          video: Object.fromEntries(
            Object.entries(x.video).filter(
              ([field]) =>
                ["duration", "width", "height", "bit_rate"].includes(field) ||
                /(?:play|download|original|origin).*addr/i.test(field),
            ),
          ),
        };
        keep.set(x.aweme_id, item);
        if (keep.size > 200) keep.delete(keep.keys().next().value);
      }
      for (const v of Object.values(x))
        if (typeof v === "object") walk(v, depth + 1);
    }
    walk(root, 0);
    window.postMessage(
      { source: "tim-video-hay", items: [...keep.values()] },
      location.origin,
    );
  }
  const original = window.fetch;
  window.fetch = async function (...args) {
    const r = await original.apply(this, args);
    try {
      if (
        r.url.includes("douyin.com/") &&
        (r.headers.get("content-type") || "").includes("json")
      )
        r.clone()
          .json()
          .then(inspect)
          .catch(() => {});
    } catch {}
    return r;
  };
  const open = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function (...args) {
    this.addEventListener("load", () => {
      try {
        if (this.responseURL.includes("douyin.com/"))
          inspect(
            this.responseType === "json"
              ? this.response
              : JSON.parse(this.responseText),
          );
      } catch {}
    });
    return open.apply(this, args);
  };
  document.addEventListener("DOMContentLoaded", () => {
    for (const script of document.querySelectorAll(
      'script[type="application/json"],script#RENDER_DATA',
    ))
      try {
        inspect(
          JSON.parse(
            script.id === "RENDER_DATA"
              ? decodeURIComponent(script.textContent)
              : script.textContent,
          ),
        );
      } catch {}
  });
  window.addEventListener("message", (event) => {
    if (
      event.source === window &&
      event.origin === location.origin &&
      event.data?.source === "tim-video-hay-reset"
    )
      keep.clear();
    if (
      event.source === window &&
      event.data?.source === "tim-video-hay-request"
    )
      window.postMessage(
        { source: "tim-video-hay", items: [...keep.values()] },
        location.origin,
      );
  });
})();
