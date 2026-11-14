using System.Reflection;
using System.Text.Json;

namespace TimVideoHay.Infrastructure;

public static class GoogleClientConfiguration
{
    public const string ResourceName = "TimVideoHay.GoogleOAuthDesktop.json";
    public static OAuthClient ParseDesktop(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024) throw new ArgumentException("Tệp cấu hình Google quá lớn.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("installed", out var installed)
                || installed.ValueKind != JsonValueKind.Object
                || !installed.TryGetProperty("client_id", out var id) || id.ValueKind != JsonValueKind.String
                || !installed.TryGetProperty("client_secret", out var secret) || secret.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Cần cấu hình Google loại Desktop app, không phải Web hoặc Service account.");
            return Validate(new(id.GetString()!, secret.GetString()!));
        }
        catch (JsonException) { throw new ArgumentException("Tệp cấu hình Google không phải JSON hợp lệ."); }
    }
    public static OAuthClient Validate(OAuthClient client)
    {
        if (string.IsNullOrEmpty(client.ClientId) || client.ClientId.Length > 300
            || !client.ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
            || !client.ClientId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            || string.IsNullOrEmpty(client.ClientSecret) || client.ClientSecret.Length > 1024
            || client.ClientSecret.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            throw new ArgumentException("Cấu hình Google Desktop không hợp lệ.");
        return client;
    }
    public static async Task<OAuthClient?> ReadBundledAsync(Assembly assembly)
    {
        await using var input = assembly.GetManifestResourceStream(ResourceName);
        if (input is null) return null;
        if (input.Length > 64 * 1024) throw new ArgumentException("Cấu hình Google đóng gói quá lớn.");
        using var reader = new StreamReader(input);
        return ParseDesktop(await reader.ReadToEndAsync());
    }
}
