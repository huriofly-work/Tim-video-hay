using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed record VideoVariant(string Id, int Width, int Height, double Fps, double Bitrate, long Size, bool SourceDownload, bool Watermarked, string Codec, bool Probed = false, bool HasAudio = true);

public static class DownloadPolicy
{
    public static double Number(JsonNode? node) => double.TryParse(node?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0;
    public static VideoVariant Variant(JsonNode format) => new(format["format_id"]!.ToString(), (int)Number(format["width"]), (int)Number(format["height"]), Number(format["fps"]), Number(format["tbr"]),
        (long)Number(format["filesize"] ?? format["filesize_approx"]), format["format_id"]!.ToString().StartsWith("download_addr", StringComparison.Ordinal),
        format["format_note"]?.ToString().Contains("watermark", StringComparison.OrdinalIgnoreCase) == true || Number(format["preference"]) == -2, format["vcodec"]?.ToString() ?? "");
    public static bool Usable(JsonNode format) => format["vcodec"]?.ToString() is "h264" or "h265" or "hevc" or "av1" or "vp9"
        && format["acodec"]?.ToString() is not (null or "none") && Number(format["preference"]) > -50;
    public static string MirrorGroup(string id) => Regex.Replace(id, @"-\d+$", "");
    public static string OutputTemplate(string folder, string stem) => Path.Combine(folder, stem).Replace("%", "%%", StringComparison.Ordinal) + ".%(ext)s";
    public static VideoVariant Best(IEnumerable<VideoVariant> variants) => variants.Where(x => x.HasAudio)
        .OrderByDescending(x => Math.Min(x.Width, x.Height)).ThenByDescending(x => (long)x.Width * x.Height)
        .ThenBy(x => x.Watermarked).ThenByDescending(x => x.Fps)
        .ThenByDescending(x => x.SourceDownload).ThenByDescending(x => x.Bitrate).ThenByDescending(x => x.Size)
        .FirstOrDefault() ?? throw new NeedsActionException("Nguồn không có bản video kèm âm thanh với codec được hỗ trợ.");
    public static IEnumerable<IGrouping<string, JsonNode>> Covers(JsonArray? thumbnails)
    {
        var candidates = (thumbnails ?? []).Where(x => x is not null
            && x["id"]?.ToString() is "cover_original_scale" or "horizontal_cover" or "source_cover"
            && DouyinHttp.Allowed(x["url"]?.ToString() ?? "")).Select(x => x!).ToArray();
        var authored = candidates.Any(x => x["id"]!.ToString() == "cover_original_scale");
        return candidates.Where(x => !authored || x["id"]!.ToString() != "source_cover")
            .OrderByDescending(x => x["id"]!.ToString() switch { "cover_original_scale" => 2, "horizontal_cover" => 1, _ => 0 })
            .GroupBy(x => x["id"]!.ToString());
    }

    public static string ReserveDirectory(VideoJob job, string stateDirectory)
    {
        if (job.Source is null) throw new InvalidOperationException("Chưa nhận diện nguồn.");
        var expected = Path.GetFullPath(Path.Combine(job.Folder, Rules.FileStem(job.Name)));
        if (job.DownloadDirectory is not null && !string.Equals(Path.GetFullPath(job.DownloadDirectory), expected, StringComparison.OrdinalIgnoreCase))
            throw new NeedsActionException("Thư mục tải đã thay đổi. Khôi phục đường dẫn công việc trước khi tiếp tục.");
        PrivateFiles.RejectLinks(expected, recursive: true);
        if (job.DownloadDirectory is not null && !job.FolderDeleted && !Directory.Exists(expected))
            throw new NeedsActionException("Không tìm thấy thư mục công việc ở đường dẫn đã lưu. Chọn vị trí mới nếu đã chuyển, hoặc xác nhận đã xóa thủ công trong nút Xóa; app chưa tạo thư mục thay thế hay tải lại từ đầu.");
        CheckDirectory(job, stateDirectory);
        Directory.CreateDirectory(expected);
        PrivateFiles.EnsureDirectory(stateDirectory);
        var legacyMarker = Path.Combine(expected, ".timvideohay-owner");
        var marker = OwnershipMarker(expected, stateDirectory);
        PrivateFiles.RejectLinks(legacyMarker); PrivateFiles.RejectLinks(marker);
        var owner = job.Id + "\n" + job.Source.Id;
        var legacyOwned = File.Exists(legacyMarker) && File.ReadAllText(legacyMarker) == owner;
        if (File.Exists(legacyMarker) && !legacyOwned) throw Collision();
        if (File.Exists(marker))
        {
            if (File.ReadAllText(marker) != owner) throw Collision();
        }
        else
        {
            if (!legacyOwned && Directory.EnumerateFileSystemEntries(expected).Any()) throw Collision();
            try
            {
                using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream); writer.Write(owner);
            }
            catch (IOException) { throw Collision(); }
        }
        if (legacyOwned) File.Delete(legacyMarker); // Private ownership is durable before removing the old marker.
        job.DownloadDirectory = expected;
        job.DirectoryIdentity ??= WindowsFileIdentity.Capture(expected);
        return expected;
    }
    public static string OwnershipMarker(string directory, string stateDirectory)
    {
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())));
        return Path.Combine(stateDirectory, digest + ".owner");
    }
    public static void CheckDirectory(VideoJob job, string stateDirectory)
    {
        var expected = Path.GetFullPath(Path.Combine(job.Folder, Rules.FileStem(job.Name)));
        PrivateFiles.RejectLinks(expected, recursive: true);
        if (File.Exists(expected)) throw Collision();
        if (!Directory.Exists(expected)) return;
        if (job.Source is null) throw Collision();
        var owner = job.Id + "\n" + job.Source.Id;
        var marker = OwnershipMarker(expected, stateDirectory);
        var legacy = Path.Combine(expected, ".timvideohay-owner");
        PrivateFiles.RejectLinks(marker); PrivateFiles.RejectLinks(legacy);
        var owned = File.Exists(marker) && File.ReadAllText(marker) == owner || File.Exists(legacy) && File.ReadAllText(legacy) == owner;
        // A persisted receipt permits resuming this job, never adopting another folder.
        if (!owned || job.DownloadDirectory is not null && !string.Equals(Path.GetFullPath(job.DownloadDirectory), expected, StringComparison.OrdinalIgnoreCase)) throw Collision();
    }
    private static NeedsActionException Collision() => new("Đã dừng: đã có thư mục hoặc tệp trùng tên video ở đích tải. Chọn tên hoặc đích khác; app không ghi đè hoặc đổi tên tự động.");
}
