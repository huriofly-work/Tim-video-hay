import { videoId, canonical } from "./domain.mjs";
export async function resolveDouyin(input, fetcher = fetch) {
  const value = String(input || "").trim();
  const match = value.match(/https:\/\/[^\s<>]+/);
  let url;
  try {
    url = new URL(match?.[0] || value);
  } catch {
    throw new Error("Nhập link video Douyin hoặc link chia sẻ Douyin.");
  }
  for (let i = 0; i < 6; i++) {
    if (
      url.protocol !== "https:" ||
      url.username ||
      url.password ||
      url.port ||
      ![
        "www.douyin.com",
        "douyin.com",
        "v.douyin.com",
        "www.iesdouyin.com",
        "iesdouyin.com",
      ].includes(url.hostname)
    )
      throw new Error("Hiện chỉ hỗ trợ link video Douyin.");
    const id =
      videoId(url.href) ||
      url.pathname.match(/^\/share\/video\/(\d{15,22})\/?$/)?.[1];
    if (id) return { platform: "douyin", id, url: canonical(id) };
    if (url.hostname !== "v.douyin.com") break;
    const response = await fetcher(url.href, {
      redirect: "manual",
      signal: AbortSignal.timeout(15000),
    });
    await response.body?.cancel();
    const target = response.headers.get("location");
    if (!target) break;
    url = new URL(target, url);
  }
  throw new Error(
    "Không nhận diện được video trong link Douyin. Dùng link trang video đầy đủ.",
  );
}
