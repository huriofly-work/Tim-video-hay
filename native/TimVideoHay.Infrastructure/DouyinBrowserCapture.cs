using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public interface IDouyinVideoBrowserReader
{
    Task<JsonNode> ReadVideoMetadataAsync(SourceVideo source, CancellationToken ct, bool readCovers = true);
    async Task<string> ReadAuthorSecUidAsync(SourceVideo source, CancellationToken ct)
    {
        var info = await ReadVideoMetadataAsync(source, ct, readCovers: false);
        var secUid = info["source_sec_uid"]?.ToString() ?? "";
        if (info["id"]?.ToString() != source.Id || !DouyinChannelReader.ValidSecUid(secUid))
            throw new NeedsActionException("Không lấy được kênh của đúng video Douyin.", JobIssueKind.SourceUnavailable);
        return secUid;
    }
    void Forget(SourceVideo source) { }
}

// Only accept responses to the site's own requests. No signature generation or browser-cookie export.
public sealed class DouyinBrowserCapture(SourceVideo source, string? knownSecUid)
{
    public static IReadOnlyList<string> UnavailableNotices { get; } = Array.AsReadOnly(new[] {
        "你要观看的视频不存在", "The video you want to watch does not exist", "The video you want to watch doesn't exist"
    });
    public static bool IsUnavailableNotice(string text) => UnavailableNotices.Contains(text.Trim().TrimEnd('.', '。'), StringComparer.Ordinal);
    public static JsonNode? VideoMetadata(Uri uri, string json, SourceVideo source)
    {
        if (uri.Scheme != "https" || uri.Host != "www.douyin.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/aweme/v1/web/aweme/detail/" || json.Length > 4 * 1024 * 1024) return null;
        var ids = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).Where(x => x.Length == 2 && x[0] == "aweme_id").Select(x => Uri.UnescapeDataString(x[1])).ToArray();
        if (ids.Length != 1 || ids[0] != source.Id) return null;
        var root = JsonNode.Parse(json); var item = root?["aweme_detail"];
        if (root?["status_code"]?.ToString() != "0" || item?["aweme_id"]?.ToString() != source.Id) return null;
        var duration = DouyinWebMetadata.ParseDuration(item, source, "Douyin app browser");
        var info = DouyinWebMetadata.Parse(item!, source); if (info is null) return null;
        if (duration is not null) info["source_duration_ms"] = duration.DurationMilliseconds;
        info["metadata_source"] = "Douyin app browser";
        return info;
    }
    public static bool HasBothCovers(JsonNode info)
    {
        var keys = DownloadPolicy.Covers(info["thumbnails"] as JsonArray).Select(c => c.Key).ToHashSet();
        return keys.Contains("cover_original_scale") && keys.Contains("horizontal_cover");
    }
    public static bool MergeVideoCovers(Uri uri, string json, SourceVideo source, JsonNode info)
    {
        if (uri.Scheme != "https" || uri.Host != "www.douyin.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.AbsolutePath != "/aweme/v1/web/aweme/detail/" || json.Length > 4 * 1024 * 1024 || info["id"]?.ToString() != source.Id) return false;
        var ids = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).Where(x => x.Length == 2 && x[0] == "aweme_id").Select(x => Uri.UnescapeDataString(x[1])).ToArray();
        if (ids.Length != 1 || ids[0] != source.Id) return false;
        var root = JsonNode.Parse(json);
        if (root?["status_code"]?.ToString() != "0" || root?["aweme_detail"]?["aweme_id"]?.ToString() != source.Id
            || root?["aweme_detail"]?["video"] is not JsonObject video) return false;
        var covers = info["thumbnails"] as JsonArray;
        if (covers is null) info["thumbnails"] = covers = new JsonArray();
        foreach (var cover in DouyinWebMetadata.CoverCandidates(video))
            if (cover is not null && !covers.Any(old => old?["id"]?.ToString() == cover["id"]?.ToString() && old?["url"]?.ToString() == cover["url"]?.ToString())) covers.Add(cover.DeepClone());
        return true;
    }
    public string? SecUid { get; private set; } = DouyinChannelReader.ValidSecUid(knownSecUid ?? "") ? knownSecUid : null;
    private (string Name, long Followers)? profile;
    private readonly Dictionary<long, (IReadOnlyList<ChannelVideo> Videos, int Ordinary, bool More, long Cursor)> pages = [];
    public bool ProfileReceived => profile is not null;
    public int PagesReceived => pages.Count;
    public bool Accept(Uri uri, string json)
    {
        if (uri.Scheme != "https" || uri.Host != "www.douyin.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || json.Length > 4 * 1024 * 1024) return false;
        var path = uri.AbsolutePath;
        if (path is not ("/aweme/v1/web/aweme/detail/" or "/aweme/v1/web/user/profile/other/" or "/aweme/v1/web/aweme/post/")) return false;
        var query = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
            .GroupBy(x => Uri.UnescapeDataString(x[0]), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(x => Uri.UnescapeDataString(x[1])).ToArray());
        string? Parameter(string key) => query.TryGetValue(key, out var v) && v.Length == 1 ? v[0] : null;
        var root = JsonNode.Parse(json);
        if (path.EndsWith("/detail/", StringComparison.Ordinal))
        {
            var item = root?["aweme_detail"];
            if (Parameter("aweme_id") != source.Id || item?["aweme_id"]?.ToString() != source.Id) return false;
            var sec = item?["author"]?["sec_uid"]?.ToString();
            if (!DouyinChannelReader.ValidSecUid(sec ?? "")) return false;
            if (SecUid is not null && SecUid != sec) throw new NeedsActionException("Douyin trả về chủ kênh khác với video nguồn. Đã dừng đọc.");
            SecUid = sec; return true;
        }
        if (SecUid is null || Parameter("sec_user_id") != SecUid) return false;
        if (path.EndsWith("/other/", StringComparison.Ordinal))
        {
            var found = DouyinChannelReader.Profile(root, SecUid);
            if (found is null) return false;
            profile = found; return true;
        }
        if (!long.TryParse(Parameter("max_cursor"), out var requested) || requested < 0 || root is null) return false;
        var batch = DouyinChannelReader.Posts(root, SecUid);
        if (pages.ContainsKey(requested)) return false;
        if (pages.Count >= 5) throw new NeedsActionException("Chưa lấy đủ 10 video sau 5 trang Douyin. Đã dừng để tránh ghi dữ liệu thiếu.");
        pages[requested] = batch; return true;
    }
    public DouyinChannelSnapshot? Result()
    {
        if (SecUid is null || profile is null) return null;
        var samples = new List<ChannelVideo>(); var seen = new HashSet<long>(); long cursor = 0; int ordinary = 0;
        long previousOldest = long.MaxValue;
        while (pages.TryGetValue(cursor, out var batch))
        {
            if (!seen.Add(cursor)) throw new NeedsActionException("Douyin lặp mốc phân trang. Chưa xác định đủ video mới nhất.");
            // A later page must not introduce a newer ordinary post. Pinned posts are handled by Latest.
            // Within-page ordering and channel identity are validated by Posts.
            if (batch.Ordinary > 0)
            {
                var distinctNew = batch.Videos.Where(v => !v.IsPinned && samples.All(old => old.Id != v.Id)).ToArray();
                if (cursor != 0 && distinctNew.Any(v => v.PublishedAt > previousOldest))
                    throw new NeedsActionException("Danh sách Douyin thay đổi trong lúc đọc. Hãy tải lại trang kênh rồi Tiếp tục.");
                previousOldest = batch.Videos.Where(v => !v.IsPinned).Min(v => v.PublishedAt);
            }
            samples.AddRange(batch.Videos); ordinary += batch.Ordinary;
            if (!batch.More || ordinary >= 10) return new(SecUid, profile.Value.Name, profile.Value.Followers, DouyinChannelReader.Latest(samples));
            if (batch.Cursor <= 0 || batch.Cursor == cursor) throw new NeedsActionException("Douyin không trả mốc đọc trang tiếp theo hợp lệ.");
            cursor = batch.Cursor;
        }
        return null;
    }
}
