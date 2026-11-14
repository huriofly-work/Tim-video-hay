namespace TimVideoHay.Core;

// Phiên bản hiển thị và gửi đi lấy từ assembly; Directory.Build.props đọc số này từ file VERSION.
public static class AppInfo
{
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string UserAgent { get; } = "TimVideoHay/" + Version;
    public static string WindowTitle { get; } = "Tìm video hay " + Version;
}
