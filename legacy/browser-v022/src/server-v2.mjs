import http from "node:http";
import fs from "node:fs/promises";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";
import { Store } from "./store.mjs";
import { Workflow } from "./workflow.mjs";
import { channelLink } from "./channels.mjs";
import { checkOutput, listFolders } from "./setup.mjs";
import { saveVideo } from "./media.mjs";
import { sheetLink, sheetTabName } from "./sheets.mjs";
const ROOT = fileURLToPath(new URL("../", import.meta.url));
export function createApplication({
  dir = path.join(ROOT, "data"),
  services = {},
} = {}) {
  // Both workflows share the file checkpoint; serialize saves so the same video
  // cannot race a second writer when the two modes use the same output folder.
  let mediaQueue = Promise.resolve();
  const sharedServices = {
    ...services,
    saveVideo: (...args) => {
      const pending = mediaQueue.then(() => {
        args[3]?.throwIfAborted();
        return (services.saveVideo || saveVideo)(...args);
      });
      mediaQueue = pending.catch(() => {});
      return pending;
    },
  };
  const store = new Store(dir),
    workflow = new Workflow(store, sharedServices),
    downloads = new Workflow(store, sharedServices, { downloadOnly: true }),
    cookie = crypto.randomBytes(32).toString("hex");
  const peers = new Map();
  let mutation = Promise.resolve();
  const runners = [workflow, downloads];
  const json = (res, value, status = 200) => {
    res.writeHead(status, {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
    });
    res.end(JSON.stringify(value));
  };
  async function body(req) {
    let value = "";
    for await (const part of req) {
      value += part;
      if (value.length > 2000000) throw new Error("Dữ liệu quá lớn.");
    }
    return value ? JSON.parse(value) : {};
  }
  const ownerOf = (url, req) => ({
    browserId:
      url.searchParams.get("browserId") || req.headers["x-browser-id"] || "",
    sessionId:
      url.searchParams.get("sessionId") || req.headers["x-app-session"] || "",
  });
  const server = http.createServer(async (req, res) => {
    try {
      const port = server.address().port,
        base = "http://127.0.0.1:" + port;
      if (req.headers.host !== "127.0.0.1:" + port)
        return json(res, { error: "Host không hợp lệ." }, 403);
      const url = new URL(req.url, base),
        p = url.pathname,
        owner = ownerOf(url, req);
      res.setHeader("X-Content-Type-Options", "nosniff");
      res.setHeader("Referrer-Policy", "no-referrer");
      res.setHeader("X-Video-App-Version", "0.2.2");
      res.setHeader(
        "Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'",
      );
      if (p.startsWith("/bridge/")) {
        if (req.headers.authorization !== "Bearer " + store.state.bridgeToken)
          return json(res, { error: "Ghép nối không hợp lệ." }, 401);
        const data = req.method === "POST" ? await body(req) : {};
        if (p === "/bridge/register" && req.method === "POST") {
          if (
            data.browserId !== owner.browserId ||
            data.sessionId !== owner.sessionId
          )
            throw new Error("Sai phiên tiện ích.");
          for (const runner of runners)
            runner.register(owner, req.headers["x-video-extension-version"]);
          peers.set(owner.sessionId, owner.browserId);
          return json(res, { ok: true });
        }
        if (peers.get(owner.sessionId) !== owner.browserId)
          return json(
            res,
            { error: "Profile này chưa gắn với tab ứng dụng." },
            403,
          );
        for (const runner of runners)
          runner.register(owner, req.headers["x-video-extension-version"]);
        if (p === "/bridge/job" && req.method === "GET") {
          const primary = workflow.next(owner);
          return json(res, primary.id ? primary : downloads.next(owner));
        }
        const runner = runners.find(
          (item) => item.current?.browserJob?.id === data.jobId,
        );
        if (p === "/bridge/active" && req.method === "POST")
          return json(res, {
            active: runner?.active(owner, data.jobId, data.lease) || false,
          });
        if (p === "/bridge/progress" && req.method === "POST")
          return json(res, { ok: runner?.progress(owner, data) || false });
        if (p === "/bridge/result" && req.method === "POST") {
          try {
            return json(res, {
              ok: runner ? await runner.receive(owner, data) : false,
            });
          } catch (error) {
            runner?.update("error", error.message);
            return json(res, { ok: false, error: error.message });
          }
        }
        return json(res, { error: "Không có thao tác." }, 404);
      }
      if (p.startsWith("/api/")) {
        if (
          !req.headers.cookie
            ?.split(";")
            .some((x) => x.trim() === "session=" + cookie)
        )
          return json(res, { error: "Mở ứng dụng để tạo phiên." }, 401);
        if (req.method !== "GET" && req.headers.origin !== base)
          return json(res, { error: "Nguồn yêu cầu không hợp lệ." }, 403);
        if (p === "/api/pair" && req.method === "GET")
          return json(res, {
            token: store.state.bridgeToken,
            version: "0.2.1",
          });
        if (p === "/api/state" && req.method === "GET")
          return json(res, {
            ...workflow.public(owner),
            downloadOnly: downloads.public(owner),
          });
        if (p === "/api/folders" && req.method === "GET")
          return json(
            res,
            await listFolders(url.searchParams.get("path") || ""),
          );
        if (req.method !== "POST")
          return json(res, { error: "Không có thao tác." }, 404);
        if (
          p !== "/api/sheets" &&
          peers.get(owner.sessionId) !== owner.browserId
        )
          return json(
            res,
            { error: "Chưa gắn tiện ích với profile đang mở ứng dụng." },
            409,
          );
        const data = await body(req);
        const task = async () => {
          if (p === "/api/sheets") {
            if (workflow.current?.status === "running")
              throw new Error(
                "Chờ công việc kết thúc hoặc dừng trước khi đổi bảng.",
              );
            const sheet = sheetLink(data.sheetUrl),
              tab = sheetTabName(data.sheetTab);
            Object.assign(store.state.settings, {
              sheetId: sheet.id,
              sheetTab: tab,
            });
            store.save();
            return { ok: true };
          }
          if (p === "/api/download/start") {
            await downloads.start(data, owner);
            return { ok: true };
          }
          if (p === "/api/download/stop") {
            downloads.stop(owner);
            return { ok: true };
          }
          if (p === "/api/download/resume") {
            await downloads.resume(owner);
            return { ok: true };
          }
          if (p === "/api/settings") {
            if (workflow.current?.status === "running")
              throw new Error(
                "Chờ công việc kết thúc hoặc dừng trước khi đổi cài đặt.",
              );
            const channel = channelLink(data.channelUrl);
            const outputDir = await (services.checkOutput || checkOutput)(
              String(data.outputDir || "").trim(),
            );
            Object.assign(store.state.settings, {
              channelUrl: channel.url,
              outputDir,
              audience: "not-kids",
            });
            store.save();
            return { ok: true };
          }
          if (p === "/api/start") {
            await workflow.start(data, owner);
            return { ok: true };
          }
          if (p === "/api/stop") {
            workflow.stop(owner);
            return { ok: true };
          }
          if (p === "/api/resume") {
            await workflow.resume(owner);
            return { ok: true };
          }
          if (p === "/api/reconcile") {
            workflow.reconcile(owner, data);
            return { ok: true };
          }
          throw new Error("Không có thao tác.");
        };
        const pending = mutation.then(task);
        mutation = pending.catch(() => {});
        return json(res, await pending);
      }
      const files = {
        "/": "index-v2.html",
        "/app-v2.js": "app-v2.js",
        "/style-v2.css": "style-v2.css",
      };
      if (req.method !== "GET" || !files[p])
        return json(res, { error: "Không tìm thấy." }, 404);
      if (p === "/")
        res.setHeader(
          "Set-Cookie",
          "session=" + cookie + "; HttpOnly; SameSite=Strict; Path=/",
        );
      res.setHeader(
        "Content-Type",
        p.endsWith(".js")
          ? "text/javascript; charset=utf-8"
          : p.endsWith(".css")
            ? "text/css; charset=utf-8"
            : "text/html; charset=utf-8",
      );
      res.setHeader("Cache-Control", "no-store");
      res.end(await fs.readFile(path.join(ROOT, "public", files[p])));
    } catch (error) {
      if (!res.headersSent) json(res, { error: error.message }, 400);
      else res.end();
    }
  });
  return { server, store, workflow, downloads };
}
if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const { server } = createApplication({
    dir: process.env.VIDEO_DATA || path.join(ROOT, "data"),
  });
  server.listen(Number(process.env.VIDEO_PORT || 43127), "127.0.0.1", () =>
    console.log(
      "Tìm video hay 0.2.2: http://127.0.0.1:" + server.address().port,
    ),
  );
  server.on("error", (error) => {
    console.error(
      error.code === "EADDRINUSE"
        ? "Cổng đang được dùng. Đóng bản cũ trước khi mở bản mới."
        : error.message,
    );
    process.exitCode = 1;
  });
}
