using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

// Unofficial consumer endpoint: no API key, OAuth token, or Douyin cookie.
public sealed class ChannelNameTranslator(HttpClient? client = null)
{
    public static bool NeedsTranslation(string text) => text.Normalize().EnumerateRunes().Any(r => Rune.IsLetter(r)
        && !(r.Value is >= 0x0041 and <= 0x007A or >= 0x00C0 and <= 0x024F or >= 0x1E00 and <= 0x1EFF
            or >= 0x2C60 and <= 0x2C7F or >= 0xA720 and <= 0xA7FF or >= 0xAB30 and <= 0xAB6F or >= 0xFB00 and <= 0xFB06));
    public async Task<string> EnglishAsync(string name, CancellationToken ct)
    {
        name = Rules.Text(name, "Tên kênh Douyin", 200);
        if (!NeedsTranslation(name)) return name;
        using var owned = client is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) : null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(12));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=en&dt=t&q=" + Uri.EscapeDataString(name));
        request.Headers.UserAgent.ParseAdd(AppInfo.UserAgent);
        try
        {
            using var response = await (client ?? owned!).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            if (!response.IsSuccessStatusCode) throw new NeedsActionException("Google Translate miễn phí chưa dịch được tên kênh. Bấm Tiếp tục để thử lại; app chưa ghi bảng.");
            if (response.Content.Headers.ContentLength > 64000) throw new NeedsActionException("Phản hồi dịch tên quá lớn.");
            await using var stream = await response.Content.ReadAsStreamAsync(budget.Token); using var bytes = new MemoryStream();
            var buffer = new byte[4096]; int n;
            while ((n = await stream.ReadAsync(buffer, budget.Token)) > 0) { if (bytes.Length + n > 64000) throw new NeedsActionException("Phản hồi dịch tên quá lớn."); bytes.Write(buffer, 0, n); }
            var json = JsonNode.Parse(bytes.ToArray());
            if (json is not JsonArray || json[0] is not JsonArray segments) throw new NeedsActionException("Google Translate trả dữ liệu dịch không hợp lệ.");
            var translated = string.Concat(segments.Select(s => s is JsonArray a && a.Count > 0 ? a[0]?.GetValue<string>() : null));
            translated = Rules.Text(WebUtility.HtmlDecode(translated), "Tên kênh tiếng Anh", 200);
            if (NeedsTranslation(translated)) throw new NeedsActionException("Kết quả dịch tên kênh vẫn còn chữ ngoài Latinh. Chưa ghi bảng; thử lại dịch vụ dịch.");
            return translated;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new NeedsActionException("Google Translate phản hồi chậm. Bấm Tiếp tục để thử lại."); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        { throw new NeedsActionException("Không đọc được bản dịch tiếng Anh của tên kênh. App chưa ghi bảng; bấm Tiếp tục để thử lại."); }
    }
}
