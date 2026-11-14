using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class DouyinChannelReader(ChannelNameTranslator translator, IDouyinBrowserReader browser) : ISourceChannelReader
{
    public static bool ValidSecUid(string value) => Regex.IsMatch(value, @"^MS4w[A-Za-z0-9_-]{16,180}$", RegexOptions.CultureInvariant);
    private static long? Integer(JsonNode? node) => node is JsonValue value
        ? value.TryGetValue<long>(out var n) ? n : value.TryGetValue<int>(out var i) ? i : null : null;
    public static (string Name, long Followers)? Profile(JsonNode? root, string secUid)
    {
        // Only a dedicated profile response is authoritative. Feed author often returns placeholder zeros.
        if (Integer(root?["status_code"]) != 0) return null;
        var user = root?["user"] ?? root?["user_info"];
        if (user?["sec_uid"]?.ToString() != secUid || Integer(user?["follower_count"]) is not long followers || followers < 0) return null;
        var name = user?["nickname"]?.ToString();
        return string.IsNullOrWhiteSpace(name) ? null : (Rules.Text(name, "Tên kênh", 200), followers);
    }
    public static (IReadOnlyList<ChannelVideo> Videos, int OrdinaryCount, bool More, long Cursor) Posts(JsonNode root, string secUid)
    {
        if (Integer(root["status_code"]) != 0 || root["aweme_list"] is not JsonArray items || items.Count == 0)
            throw Missing("danh sách video công khai của kênh");
        var more = root["has_more"] is JsonValue flag && flag.TryGetValue<bool>(out var b) ? b
            : Integer(root["has_more"]) is long n && n is 0 or 1 ? n == 1 : throw Missing("trạng thái phân trang kênh");
        var cursor = Integer(root["max_cursor"]) ?? throw Missing("mốc phân trang kênh");
        var videos = new List<ChannelVideo>(); int ordinary = 0; long previous = long.MaxValue;
        foreach (var item in items)
        {
            if (item?["author"]?["sec_uid"]?.ToString() != secUid) throw Missing("đúng chủ kênh của danh sách video");
            // Photo/carousel posts cannot be classified by their background music duration.
            if (Integer(item?["aweme_type"]) is 68 or 150 || item?["images"] is JsonArray { Count: > 0 }) continue;
            if (item?["video"] is not JsonObject) throw Missing("thời lượng của một video trong danh sách kênh");
            var id = item?["aweme_id"]?.ToString() ?? "";
            if (!Regex.IsMatch(id, @"^\d{15,22}$") || Integer(item?["create_time"]) is not long time || time <= 0)
                throw Missing("ID/thời gian đăng video kênh");
            var pinned = Integer(item?["is_top"]) is > 0 || item?["is_top"] is JsonValue top && top.TryGetValue<bool>(out var isTop) && isTop;
            if (!pinned)
            {
                if (time > previous) throw Missing("thứ tự 10 video mới nhất");
                previous = time; ordinary++;
            }
            videos.Add(new(id, time, Integer(item?["video"]?["duration"]) ?? 0, pinned));
        }
        return (videos, ordinary, more, cursor);
    }
    public static IReadOnlyList<ChannelVideo> Latest(IEnumerable<ChannelVideo> input)
    {
        var groups = input.GroupBy(v => v.Id).ToArray();
        if (groups.Any(g => g.Select(v => (v.PublishedAt, v.DurationMilliseconds)).Distinct().Count() > 1)) throw Missing("thời lượng nhất quán của video kênh");
        var result = groups.Select(g => g.First()).OrderByDescending(v => v.PublishedAt).ThenByDescending(v => v.Id, StringComparer.Ordinal).Take(10).ToArray();
        if (result.Length == 0 || result.Any(v => v.DurationMilliseconds <= 0)) throw Missing("thời lượng các video mới nhất");
        return result;
    }
    public async Task<SourceChannelMetadata> ReadAsync(VideoJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var raw = await browser.ReadAsync(job.Source ?? throw Missing("video nguồn"), null, ct);
        if (!ValidSecUid(raw.SecUid) || raw.Followers < 0 || string.IsNullOrWhiteSpace(raw.Name)) throw Missing("dữ liệu kênh hợp lệ");
        var latest = Latest(raw.Videos);
        var english = await translator.EnglishAsync(raw.Name, ct);
        return new(raw.SecUid, raw.Name, english, raw.Followers, latest, DateTimeOffset.UtcNow);
    }
    private static NeedsActionException Missing(string part) => new("Chưa lấy được " + part + " từ Douyin. Không ghi 0 hoặc đoán dạng nội dung. Mở Douyin trong app để đăng nhập/xác minh rồi Tiếp tục; phiên đăng nhập không bảo đảm mọi yêu cầu đều được Douyin chấp nhận.");
}
