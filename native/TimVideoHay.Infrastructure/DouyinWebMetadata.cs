using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

// Normalizes exact-video metadata from public APIs or the site's own browser responses.
public static class DouyinWebMetadata
{
    private const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public static SourceMetadata? ParseDuration(JsonNode? item, SourceVideo source, string provider)
    {
        if (item?["aweme_id"]?.ToString() != source.Id || item?["video"]?["duration"] is not JsonValue raw
            || !raw.TryGetValue<long>(out var ms) || ms <= 0) return null;
        return new(source.Id, ms, provider, DateTimeOffset.UtcNow);
    }
    public static JsonNode? Parse(JsonNode detail, SourceVideo source)
    {
        var video = detail["video"] as JsonObject;
        if (video is null) return null;
        var formats = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var originalId = video["download_addr"]?["uri"]?.ToString() ?? video["play_addr"]?["uri"]?.ToString();
        if (!string.IsNullOrWhiteSpace(originalId) && originalId.Length <= 200)
            formats.Add(new JsonObject { ["format_id"] = "download_addr-original-0",
                ["url"] = "https://www.iesdouyin.com/aweme/v1/play/?video_id=" + Uri.EscapeDataString(originalId) + "&ratio=default&line=0",
                ["ext"] = "mp4", ["vcodec"] = "h264", ["acodec"] = "aac", ["width"] = video["width"]?.DeepClone(), ["height"] = video["height"]?.DeepClone(),
                ["preference"] = -1, ["format_note"] = "Source download (ratio=default)" });
        if (!string.IsNullOrWhiteSpace(originalId) && originalId.Length <= 200)
            formats.Add(new JsonObject { ["format_id"] = "download_addr-improved-0",
                ["url"] = "https://www.douyin.com/aweme/v1/play/?video_id=" + Uri.EscapeDataString(originalId) + "&ratio=default&line=0&improve_bitrate=1&watermark=0&media_type=4&vr_type=0&is_play_url=1&source=PackSourceEnum_AWEME_DETAIL",
                ["ext"] = "mp4", ["vcodec"] = "h264", ["acodec"] = "aac", ["width"] = video["width"]?.DeepClone(), ["height"] = video["height"]?.DeepClone(),
                ["preference"] = -1, ["format_note"] = "Source download (improve_bitrate=1)" });
        void Address(JsonNode? address, string id, string codec, double bitrate = 0, double fps = 0, bool watermarked = false)
        {
            if (address?["url_list"] is not JsonArray urls || address["url_key"]?.ToString().Contains("bytevc2", StringComparison.Ordinal) == true) return;
            var mirror = 0;
            foreach (var url in urls)
            {
                if (!Uri.TryCreate(url?.ToString(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || !seen.Add(uri.AbsoluteUri)) continue;
                // Adaptive DASH video-only tracks are not standalone videos.
                var videoOnly = uri.AbsolutePath.EndsWith("/media-video-hvc1/", StringComparison.Ordinal) || uri.AbsolutePath.EndsWith("/media-video-avc1/", StringComparison.Ordinal);
                formats.Add(new JsonObject { ["format_id"] = id + "-" + mirror++, ["url"] = uri.AbsoluteUri, ["ext"] = "mp4", ["vcodec"] = codec,
                    ["acodec"] = videoOnly ? "none" : "aac", ["width"] = address["width"]?.DeepClone(), ["height"] = address["height"]?.DeepClone(),
                    ["filesize"] = address["data_size"]?.DeepClone(), ["tbr"] = bitrate / 1000, ["fps"] = fps,
                    ["preference"] = watermarked ? -2 : -1, ["format_note"] = watermarked ? "watermarked" : "Web source" });
            }
        }
        Address(video["download_addr"], "download_addr-web", "h264", watermarked: video["has_watermark"]?.ToString() == "true");
        foreach (var rate in video["bit_rate"]?.AsArray() ?? [])
        {
            if (rate is null || DownloadPolicy.Number(rate["is_bytevc2"]) > 0) continue;
            var bitrate = DownloadPolicy.Number(rate["bit_rate"]);
            var codec = DownloadPolicy.Number(rate["is_bytevc1"] ?? rate["is_h265"]) > 0 ? "h265" : "h264";
            Address(rate["play_addr"], "web-" + codec + "_" + bitrate.ToString("0", System.Globalization.CultureInfo.InvariantCulture), codec, bitrate, DownloadPolicy.Number(rate["FPS"]));
        }
        Address(video["play_addr"], "web-play", DownloadPolicy.Number(video["is_h265"]) > 0 ? "h265" : "h264");
        Address(video["play_addr_h264"], "web-play-h264", "h264"); Address(video["play_addr_265"] ?? video["play_addr_bytevc1"], "web-play-h265", "h265");
        if (formats.Count == 0) return null;
        var covers = CoverCandidates(video);
        return new JsonObject { ["id"] = source.Id, ["title"] = detail["desc"]?.ToString() ?? source.Id, ["webpage_url"] = source.CanonicalUrl,
            ["source_sec_uid"] = detail["author"]?["sec_uid"]?.ToString(), ["extractor"] = "Douyin", ["extractor_key"] = "Douyin", ["duration"] = DownloadPolicy.Number(video["duration"]) / 1000,
            ["formats"] = formats, ["thumbnails"] = covers, ["http_headers"] = new JsonObject { ["User-Agent"] = Agent, ["Referer"] = source.CanonicalUrl } };
    }
    public static JsonArray CoverCandidates(JsonNode video)
    {
        var covers = new JsonArray();
        void Add(string field, string group)
        {
            foreach (var url in video[field]?["url_list"]?.AsArray() ?? [])
                covers.Add(new JsonObject { ["id"] = group, ["source_field"] = field, ["url"] = url?.ToString() });
        }
        // dynamic_cover can be the unresized, static publisher image. Only use
        // it when its asset URI matches the selected poster; validate bytes later.
        var authoredUri = video["cover_original_scale"]?["uri"]?.ToString();
        if (!string.IsNullOrWhiteSpace(authoredUri) && video["dynamic_cover"]?["uri"]?.ToString() == authoredUri)
            Add("dynamic_cover", "cover_original_scale");
        Add("cover_original_scale", "cover_original_scale");
        Add("y_cover", "cover_original_scale");
        // Some feed responses (e.g. Alien 03) omit the explicit poster fields.
        // Their display cover is still the publisher's image when its asset URI
        // matches dynamic_cover and differs from the raw origin_cover frame.
        // Keep this separate so an explicit poster from another response wins.
        var displayUri = video["cover"]?["uri"]?.ToString();
        if (!covers.Any() && !string.IsNullOrWhiteSpace(displayUri)
            && displayUri == video["dynamic_cover"]?["uri"]?.ToString()
            && displayUri != video["origin_cover"]?["uri"]?.ToString())
        {
            Add("dynamic_cover", "source_cover");
            Add("cover", "source_cover");
        }
        // Separate horizontal posters exposed by Select/Jingxuan; preserve
        // their actual aspect ratio, which is not necessarily 16:9.
        // Never substitute origin_cover or an unverified cover frame.
        Add("y_cover_hor", "horizontal_cover");
        Add("universal_cover_horizontal169_big", "horizontal_cover");
        Add("universal_cover_horizontal169", "horizontal_cover");
        return covers;
    }
}
