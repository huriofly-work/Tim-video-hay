using System.Security.Cryptography;
using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public static class ToolIntegrity
{
    public static string Verify(string path)
    {
        PrivateFiles.RejectLinks(path);
        if (!File.Exists(path)) throw new NeedsActionException("Bản cài thiếu công cụ đọc video. Dùng bản đóng gói đầy đủ.");
        using var resource = typeof(ToolIntegrity).Assembly.GetManifestResourceStream("TimVideoHay.ToolManifest.json")
            ?? throw new NeedsActionException("Bản cài thiếu cấu hình xác minh công cụ.");
        using var reader = new StreamReader(resource);
        var expected = JsonNode.Parse(reader.ReadToEnd())?["ffprobe"]?["sha256"]?.ToString();
        using var input = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(input));
        if (Path.GetFileName(path) != "ffprobe.exe" || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new NeedsActionException("Công cụ đọc video không khớp bản phát hành. App đã chặn chạy; cài lại từ nguồn tin cậy.");
        return path;
    }
}
