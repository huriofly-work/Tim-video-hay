using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed record OAuthClient(string ClientId, string ClientSecret);
public sealed record OAuthToken(string AccessToken, string RefreshToken, DateTimeOffset Expires, string Scope);
public sealed record AuthResult(string CredentialId, string? SpreadsheetId);
// defaultClient chỉ dùng khi test; app luôn lấy client đã nhập và lưu trong hồ sơ.
public sealed class GoogleAuth(ISecretStore secrets, HttpClient http, OAuthClient? defaultClient = null)
{
    public const string DriveScope = "https://www.googleapis.com/auth/drive.file";
    public const string YouTubeScopes = "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly";
    public const string YouTubeDeleteScope = "https://www.googleapis.com/auth/youtube.force-ssl";
    public async Task<bool> HasYouTubeDeleteScopeAsync(string credential, CancellationToken ct)
    {
        var raw = await secrets.ReadAsync(credential, ct);
        if (raw is null) return false;
        var scopes = JsonSerializer.Deserialize<OAuthToken>(raw)?.Scope.Split(' ') ?? [];
        return scopes.Contains(YouTubeDeleteScope) || scopes.Contains("https://www.googleapis.com/auth/youtube");
    }
    public async Task AdoptYouTubeDeleteGrantAsync(string existingCredential, string grantedCredential, CancellationToken ct)
    {
        if (!await HasYouTubeDeleteScopeAsync(grantedCredential, ct)) throw new NeedsAuthException("Google chưa cấp quyền xóa video.");
        await AdoptVerifiedGrantAsync(existingCredential, grantedCredential, ct);
    }
    // The caller must verify the same spreadsheet/channel through Google before
    // replacing a grant. Keeping the key repairs defaults and every job snapshot.
    public async Task AdoptVerifiedGrantAsync(string existingCredential, string grantedCredential, CancellationToken ct)
    {
        if (existingCredential == grantedCredential) throw new ArgumentException("Quyền thay thế phải có khóa riêng.");
        await refreshLock.WaitAsync(ct);
        try
        {
            var token = await secrets.ReadAsync(grantedCredential, ct) ?? throw new NeedsAuthException("Không tìm thấy quyền vừa cấp.");
            // Caller verifies the same channel first. Keep the shared key so future jobs
            // and existing snapshots can use this upgrade without another consent flow.
            await secrets.WriteAsync(existingCredential, token, ct);
            await secrets.DeleteAsync(grantedCredential, ct);
        }
        finally { refreshLock.Release(); }
    }
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    public async Task<bool> HasClientAsync() => await secrets.ReadAsync("oauth-client") is not null || defaultClient is not null;
    public async Task ImportClientAsync(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > 64 * 1024) throw new ArgumentException("Tệp OAuth quá lớn.");
        var client = GoogleClientConfiguration.ParseDesktop(await File.ReadAllTextAsync(path));
        var previous = await secrets.ReadAsync("oauth-client");
        if (previous is not null && JsonSerializer.Deserialize<OAuthClient>(previous)?.ClientId != client.ClientId)
            throw new NeedsActionException("App đang có OAuth client khác. Để đổi project, hãy ngắt kết nối cả bảng và YouTube trước rồi dùng hồ sơ dữ liệu mới; không thay client khi còn công việc chưa hoàn tất.");
        await secrets.WriteAsync("oauth-client", JsonSerializer.Serialize(client));
    }
    public async Task<OAuthClient> ConfiguredClientAsync(CancellationToken ct = default)
    {
        var stored = await secrets.ReadAsync("oauth-client", ct);
        if (stored is not null) return GoogleClientConfiguration.Validate(JsonSerializer.Deserialize<OAuthClient>(stored)
            ?? throw new NeedsAuthException("Cấu hình Google đã lưu không hợp lệ."));
        return defaultClient is not null ? GoogleClientConfiguration.Validate(defaultClient)
            : throw new NeedsAuthException("Bản cài chưa có cấu hình kết nối Google. Cần chuẩn bị cấu hình ứng dụng một lần trong Thiết lập nâng cao.");
    }
    public async Task<AuthResult> ConnectAsync(bool picker, CancellationToken ct, bool allowYouTubeDeletion = false)
    {
        var requestedScopes = picker ? DriveScope : allowYouTubeDeletion ? YouTubeDeleteScope : YouTubeScopes;
        var client = await ConfiguredClientAsync(ct);
        // Pin the selected project before granting any token. A different client
        // must not silently change the project of existing credentials.
        await secrets.WriteAsync("oauth-client", JsonSerializer.Serialize(client), ct);
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener(); listener.Prefixes.Add(redirect); listener.Start();
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["scope"] = requestedScopes, ["state"] = state,
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256",
            ["access_type"] = "offline", ["prompt"] = "consent select_account", ["include_granted_scopes"] = "false"
        };
        if (picker) { parameters["trigger_onepick"] = "true"; parameters["mimetypes"] = "application/vnd.google-apps.spreadsheet"; parameters["allow_multiple"] = "false"; }
        var query = string.Join("&", parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        Process.Start(new ProcessStartInfo("https://accounts.google.com/o/oauth2/v2/auth?" + query) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        HttpListenerContext callback;
        while (true)
        {
            callback = await listener.GetContextAsync().WaitAsync(timeout.Token);
            if (callback.Request.Url?.AbsolutePath == "/" && callback.Request.QueryString["state"] == state) break;
            callback.Response.StatusCode = 400; callback.Response.Close();
        }
        var code = callback.Request.QueryString["code"];
        var error = callback.Request.QueryString["error"];
        var picked = callback.Request.QueryString["picked_file_ids"];
        var page = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>Tìm video hay</title><p>Đã nhận phản hồi Google. Bạn có thể quay lại app Tìm video hay.</p>");
        callback.Response.ContentType = "text/html; charset=utf-8"; callback.Response.ContentLength64 = page.Length;
        await callback.Response.OutputStream.WriteAsync(page, timeout.Token); callback.Response.Close(); listener.Stop();
        if (error is not null || string.IsNullOrEmpty(code)) throw new NeedsAuthException("Google chưa cấp quyền hoặc bạn đã hủy đăng nhập.");
        if (picker && (string.IsNullOrWhiteSpace(picked) || picked.Contains(',') || picked.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))))
            throw new NeedsActionException("Chọn đúng một bảng Google Sheets.");
        var token = await ExchangeAsync(new() { ["client_id"] = client.ClientId, ["client_secret"] = client.ClientSecret,
            ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code" }, null, timeout.Token);
        var required = requestedScopes.Split(' ');
        if (required.Any(scope => !token.Scope.Split(' ').Contains(scope))) throw new NeedsAuthException("Google chưa cấp đủ quyền cần thiết. Kết nối lại và chấp nhận quyền của chức năng đó.");
        if (string.IsNullOrEmpty(token.RefreshToken)) throw new NeedsAuthException("Google không trả quyền dùng lâu dài. Thu hồi quyền ứng dụng tại tài khoản Google rồi kết nối lại.");
        var credential = (picker ? "sheets-" : "youtube-") + Guid.NewGuid().ToString("N");
        await secrets.WriteAsync(credential, JsonSerializer.Serialize(token), ct);
        return new(credential, picker ? picked : null);
    }
    public async Task<string> AccessTokenAsync(string credential, CancellationToken ct, string? rejectedAccessToken = null)
    {
        await refreshLock.WaitAsync(ct);
        try
        {
            var token = JsonSerializer.Deserialize<OAuthToken>(await secrets.ReadAsync(credential, ct) ?? throw new NeedsAuthException("Chưa kết nối Google hoặc kết nối đã bị xóa. Kết nối lại đúng bảng/kênh của công việc."))!;
            if (token.Expires > DateTimeOffset.UtcNow.AddMinutes(2) && token.AccessToken != rejectedAccessToken) return token.AccessToken;
            if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new NeedsAuthException("Kết nối Google thiếu quyền dùng lâu dài. Kết nối lại đúng bảng/kênh của công việc.");
            var client = await ConfiguredClientAsync(ct);
            token = await ExchangeAsync(new() { ["client_id"] = client.ClientId, ["client_secret"] = client.ClientSecret,
                ["refresh_token"] = token.RefreshToken, ["grant_type"] = "refresh_token" }, token, ct);
            await secrets.WriteAsync(credential, JsonSerializer.Serialize(token), ct);
            return token.AccessToken;
        }
        finally { refreshLock.Release(); }
    }
    private async Task<OAuthToken> ExchangeAsync(Dictionary<string, string> fields, OAuthToken? old, CancellationToken ct)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode)
        {
            string? error = null;
            try { error = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["error"]?.GetValue<string>(); }
            catch (JsonException) { }
            if (error == "invalid_grant") throw new NeedsAuthException("Google đã thu hồi hoặc hết hạn quyền dùng lâu dài. Kết nối lại đúng bảng/kênh; quyền của project Testing có thể hết hạn sau 7 ngày.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new RetryLaterException("Google tạm giới hạn việc làm mới kết nối. Giữ quyền đã lưu và chờ thử lại.", response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1)), "google-api");
            if ((int)response.StatusCode >= 500 || error is "temporarily_unavailable" or "server_error")
                throw new HttpRequestException("Google tạm thời chưa làm mới được kết nối. Quyền đã lưu được giữ để thử lại.");
            throw new NeedsActionException($"Không thể làm mới kết nối Google (HTTP {(int)response.StatusCode}). Kiểm tra cấu hình OAuth của ứng dụng; quyền đã lưu được giữ nguyên.", JobIssueKind.Configuration);
        }
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        return new(json["access_token"]!.GetValue<string>(), json["refresh_token"]?.GetValue<string>() ?? old?.RefreshToken ?? "",
            DateTimeOffset.UtcNow.AddSeconds(json["expires_in"]!.GetValue<int>()), json["scope"]?.GetValue<string>() ?? old?.Scope ?? "");
    }
    public async Task DisconnectLocalAsync(string credential, CancellationToken ct = default) => await secrets.DeleteAsync(credential, ct);
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class GoogleTransport(GoogleAuth auth, HttpClient http)
{
    public async Task RequireYouTubeDeletePermissionAsync(string credential, CancellationToken ct)
    {
        if (!await auth.HasYouTubeDeleteScopeAsync(credential, ct))
            throw new NeedsAuthException("Cần cấp thêm quyền xóa video YouTube. Kết nối lại đúng kênh gốc; các kết quả đã xóa khác được giữ.");
    }
    public async Task<HttpResponseMessage> SendAsync(string credential, HttpRequestMessage request, CancellationToken ct)
    {
        ValidateGoogleUri(request.RequestUri!);
        var accessToken = await auth.AccessTokenAsync(credential, ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            var renewed = await auth.AccessTokenAsync(credential, ct, rejectedAccessToken: accessToken);
            if (request.Method != HttpMethod.Get)
                throw new HttpRequestException("Đã làm mới phiên Google. Cần đối soát kết quả trước khi tiếp tục thao tác đang dở.");
            using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
            response = await http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            { response.Dispose(); throw new NeedsAuthException("Google vẫn từ chối quyền sau khi tự làm mới. Kết nối lại đúng bảng/kênh của công việc."); }
        }
        return response;
    }
    public async Task<JsonNode> JsonAsync(string credential, HttpMethod method, string url, object? body, CancellationToken ct)
    {
        // Only reads are replayed automatically. Mutations reconcile at the workflow layer.
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await SendAsync(credential, request, ct);
            if (method == HttpMethod.Get && attempt < 3 && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
                await Task.Delay(delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay, ct); continue;
            }
            if (method == HttpMethod.Get && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
                throw new HttpRequestException("Google tạm thời chưa trả được kết quả đọc.");
            await EnsureSuccessAsync(response, ct);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) ?? new JsonObject();
        }
    }
    public static void ValidateGoogleUri(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || !(uri.Host == "googleapis.com" || uri.Host.EndsWith(".googleapis.com", StringComparison.Ordinal)))
            throw new NeedsActionException("Google trả địa chỉ phiên upload không hợp lệ; đã dừng để bảo vệ token.");
    }
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new RetryLaterException("Google giới hạn tần suất truy cập. Giữ checkpoint và chờ trước khi thử lại.", response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5)), "google-api");
        string? reason = null;
        try { reason = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["error"]?["errors"]?[0]?["reason"]?.GetValue<string>(); } catch (JsonException) { }
        var safeReason = reason is not null && reason.All(c => char.IsAsciiLetterOrDigit(c)) ? $" ({reason})" : "";
        throw new NeedsActionException($"Google API trả lỗi {(int)response.StatusCode}{safeReason}. Kiểm tra quyền, API đã bật, quota và đích đã chọn. Kết quả trước lỗi vẫn được giữ để đối soát.");
    }
}
