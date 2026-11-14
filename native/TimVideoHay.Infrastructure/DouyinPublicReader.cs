using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

// Public metadata only. Reads only the dedicated anonymous guest ticket, never account credentials.
public sealed class DouyinPublicReader : IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim visitorGate = new(1, 1);
    private readonly ISecretStore? guestStore;
    private bool guestLoaded;
    private readonly TimeSpan spacing;
    private DateTimeOffset nextRequest, blockedUntil, webUnavailableUntil, visitorExpires;
    private DateTimeOffset browserRequiredUntil;
    private bool browserLoginRequired;
    private DateTimeOffset guestRefreshedAt;
    private bool webReceivedEmpty;
    private string? visitor;
    private readonly Dictionary<string, (JsonNode Info, DateTimeOffset Time, bool QualityChecked, bool CoversChecked)> cache = [];
    private static readonly string[] Feeds = ["https://api5-normal-c-hl.amemv.com/aweme/v1/feed/", "https://aweme.snssdk.com/aweme/v1/feed/"];
    private const string MobileAgent = "com.ss.android.ugc.aweme/290101 (Linux; U; Android 10; zh_CN; Pixel 4; Build/QQ3A.200805.001; Cronet/TTNetVersion:5f9037be 2023-01-13 QuicVersion:4668bb42 2022-11-21)";
    public DouyinPublicReader(HttpClient? client = null, TimeSpan? minimumSpacing = null, ISecretStore? guestStore = null)
    { http = client ?? DouyinHttp.Create(); ownsClient = client is null; spacing = minimumSpacing ?? TimeSpan.FromSeconds(2); this.guestStore = guestStore; }
    public void EnsureAllowed()
    {
        if (DateTimeOffset.UtcNow < blockedUntil)
            throw new RetryLaterException($"Douyin đang giới hạn truy cập. Chờ đến {blockedUntil.ToLocalTime():HH:mm:ss}; phiên và dữ liệu được giữ.", blockedUntil, "douyin-metadata");
    }
    public void SuspendForChallenge(DateTimeOffset? retryAt = null) => Pause(retryAt is { } date ? new System.Net.Http.Headers.RetryConditionHeaderValue(date) : null);
    public async Task<JsonNode?> ReadAsync(SourceVideo source, bool download, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); EnsureAllowed();
        JsonNode? info = null; var qualityChecked = false;
        webReceivedEmpty = false; DouyinGuestVerificationException? webChallenge = null;
        if (cache.TryGetValue(source.Id, out var old) && DateTimeOffset.UtcNow - old.Time < TimeSpan.FromMinutes(2))
        {
            info = old.Info.DeepClone(); qualityChecked = old.QualityChecked;
            if (!download || old.QualityChecked && old.CoversChecked) return info;
        }
        if (DateTimeOffset.UtcNow < browserRequiredUntil) throw GuestVerificationRequired(browserLoginRequired);
        if (download && !qualityChecked && DateTimeOffset.UtcNow >= webUnavailableUntil)
        {
            try
            {
                await VisitorAsync(ct);
                var web = await DetailAsync(source, "6383", ct);
                if (web is not null) info = Normalize(web, source, "Douyin public web", download);
            }
            catch (DouyinGuestVerificationException ex)
            {
                // Web identity/challenge does not establish that the independent public
                // feed is unusable. No account identity or cookie is sent to these feeds.
                webChallenge = ex; webUnavailableUntil = DateTimeOffset.UtcNow.AddMinutes(15); browserRequiredUntil = default;
            }
            qualityChecked = true;
        }
        if (download && !(info?["formats"] is JsonArray renditions
            && renditions.Any(f => f is not null && DownloadPolicy.Usable(f) && DouyinHttp.Allowed(f["url"]?.ToString() ?? "")))) info = null;
        if (info is null)
        {
            DouyinGuestVerificationException? challenge = null;
            foreach (var feed in Feeds)
            {
                JsonNode? item;
                try { item = await FeedAsync(feed, source, "1128", ct); }
                catch (DouyinGuestVerificationException ex) when (download) { challenge = ex; continue; }
                info = item is null ? null : Normalize(item, source, "Douyin public feed", download);
                if (download && !(info?["formats"] is JsonArray formats
                    && formats.Any(f => f is not null && DownloadPolicy.Usable(f) && DouyinHttp.Allowed(f["url"]?.ToString() ?? "")))) info = null;
                if (info is not null) break;
            }
            if (info is null && challenge is not null) webChallenge ??= challenge;
        }
        if (info is null && webReceivedEmpty && visitor is not null && guestRefreshedAt < DateTimeOffset.UtcNow.AddDays(-1)
            && DateTimeOffset.UtcNow >= webUnavailableUntil)
        {
            // Restore the old bounded guest renewal for an empty successful web reply.
            // UIFID, 403, 429 and network failures must not register identities in a loop.
            guestRefreshedAt = DateTimeOffset.UtcNow; visitorExpires = default;
            try
            {
                await VisitorAsync(ct);
                var web = await DetailAsync(source, "6383", ct);
                if (web is not null) info = Normalize(web, source, "Douyin public web", download);
            }
            catch (DouyinGuestVerificationException ex) { webChallenge ??= ex; browserRequiredUntil = default; }
        }
        if (download && !(info?["formats"] is JsonArray refreshedFormats
            && refreshedFormats.Any(f => f is not null && DownloadPolicy.Usable(f) && DouyinHttp.Allowed(f["url"]?.ToString() ?? "")))) info = null;
        if (info is null) { if (webChallenge is not null) throw webChallenge; return null; }
        browserRequiredUntil = default;
        if (download && !DouyinBrowserCapture.HasBothCovers(info))
        {
            // The old Select web response can carry the authored horizontal poster.
            // Keep the already chosen video renditions and merge posters only.
            if (info["metadata_source"]?.ToString() == "Douyin public web" && DateTimeOffset.UtcNow >= webUnavailableUntil)
            {
                try { var selected = await DetailAsync(source, "568863", ct); if (selected is not null) MergeCovers(selected, info); }
                catch (DouyinGuestVerificationException) { browserRequiredUntil = default; }
            }
            // Only request the explicit authored posters, never substitute cover frames.
            foreach (var feed in Feeds)
            {
                if (DouyinBrowserCapture.HasBothCovers(info)) break;
                JsonNode? item;
                try { item = await FeedAsync(feed, source, "568863", ct); }
                // Optional posters must not turn an already usable video into an account request.
                catch (DouyinGuestVerificationException) { browserRequiredUntil = default; break; }
                if (item is not null) MergeCovers(item, info);
                if (DouyinBrowserCapture.HasBothCovers(info)) break;
            }
        }
        Remember(source, info, qualityChecked || download, coversChecked: download);
        return info;
    }
    public void Remember(SourceVideo source, JsonNode info, bool qualityChecked = true, bool coversChecked = true)
    {
        if (info["id"]?.ToString() != source.Id) return;
        browserRequiredUntil = default; browserLoginRequired = false;
        if (cache.Count >= 32) cache.Remove(cache.MinBy(x => x.Value.Time).Key);
        cache[source.Id] = (info.DeepClone(), DateTimeOffset.UtcNow, qualityChecked, coversChecked);
    }
    public string? Author(SourceVideo source) => cache.TryGetValue(source.Id, out var value) && DateTimeOffset.UtcNow - value.Time < TimeSpan.FromMinutes(2)
        && DouyinChannelReader.ValidSecUid(value.Info["source_sec_uid"]?.ToString() ?? "") ? value.Info["source_sec_uid"]!.ToString() : null;
    public void Forget(SourceVideo source) => cache.Remove(source.Id);
    private static JsonNode? Normalize(JsonNode item, SourceVideo source, string origin, bool download = false)
    {
        var duration = DouyinWebMetadata.ParseDuration(item, source, origin);
        if (item["aweme_id"]?.ToString() != source.Id || duration is null && !download) return null;
        var info = DouyinWebMetadata.Parse(item, source) ?? new JsonObject { ["id"] = source.Id, ["formats"] = new JsonArray(), ["thumbnails"] = new JsonArray() };
        if (duration is not null) info["source_duration_ms"] = duration.DurationMilliseconds;
        info["metadata_source"] = origin;
        info["source_sec_uid"] = item["author"]?["sec_uid"]?.ToString();
        return info;
    }
    private async Task<JsonNode?> DetailAsync(SourceVideo source, string aid, CancellationToken ct)
    {
        var json = await JsonAsync("https://www.douyin.com/aweme/v1/web/aweme/detail/?aweme_id=" + Uri.EscapeDataString(source.Id)
            + "&device_platform=webapp&aid=" + aid + "&channel=channel_pc_web&pc_client_type=1&version_code=190500&version_name=19.5.0&codec_type=1&support_h265=1&support_dash=1", false, ct);
        return json?["status_code"]?.ToString() == "0" && json?["aweme_detail"]?["aweme_id"]?.ToString() == source.Id ? json["aweme_detail"] : null;
    }
    private async Task<JsonNode?> FeedAsync(string endpoint, SourceVideo source, string aid, CancellationToken ct)
    {
        var json = await JsonAsync(endpoint + "?aweme_id=" + Uri.EscapeDataString(source.Id) + "&aid=" + aid, true, ct);
        return json?["status_code"]?.ToString() == "0" && json["aweme_list"] is JsonArray items
            ? items.FirstOrDefault(item => item?["aweme_id"]?.ToString() == source.Id) : null;
    }
    private static void MergeCovers(JsonNode item, JsonNode info)
    {
        if (item["aweme_id"]?.ToString() != info["id"]?.ToString() || item["video"] is not JsonObject video) return;
        var covers = info["thumbnails"] as JsonArray;
        if (covers is null) info["thumbnails"] = covers = new JsonArray();
        foreach (var cover in DouyinWebMetadata.CoverCandidates(video))
            if (cover is not null && !covers.Any(c => c?["id"]?.ToString() == cover["id"]?.ToString() && c?["url"]?.ToString() == cover["url"]?.ToString())) covers.Add(cover.DeepClone());
    }
    private async Task VisitorAsync(CancellationToken ct)
    {
        await visitorGate.WaitAsync(ct);
        try
        {
        await LoadGuestAsync(ct);
        if (DateTimeOffset.UtcNow < visitorExpires) return;
        // One anonymous registration per day; a failed registration is not retried in a loop.
        visitorExpires = DateTimeOffset.UtcNow.AddDays(1); visitor = null;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://ttwid.bytedance.com/ttwid/union/register/") {
            Content = new StringContent("{\"region\":\"cn\",\"aid\":1768,\"needFid\":false,\"service\":\"www.iesdouyin.com\",\"migrate_info\":{\"ticket\":\"\",\"source\":\"node\"},\"cbUrlProtocol\":\"https\",\"union\":true}", Encoding.UTF8, "application/json") };
        using var response = await SendAsync(request, ct);
        if (response is not null && response.IsSuccessStatusCode && response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
        // Read only the anonymous ttwid, never any session/account cookie.
        foreach (var header in headers)
        {
            var first = header.Split(';')[0];
            if (!first.StartsWith("ttwid=", StringComparison.Ordinal)) continue;
            var value = first[6..];
            if (ValidVisitor(value)) visitor = value;
        }
        }
        if (guestStore is not null)
        {
            try { await guestStore.WriteAsync("guest-douyin", JsonSerializer.Serialize(new GuestTicket(visitor, visitorExpires, guestRefreshedAt)), ct); }
            catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException) { }
        }
        }
        finally { visitorGate.Release(); }
    }
    private sealed record GuestTicket(string? Token, DateTimeOffset Expires, DateTimeOffset RefreshedAt = default);
    // For the isolated guest WebView only: no network and no account cookie reads.
    public async Task<string?> CachedGuestTokenAsync(CancellationToken ct)
    {
        await visitorGate.WaitAsync(ct);
        try { await LoadGuestAsync(ct); return visitorExpires > DateTimeOffset.UtcNow ? visitor : null; }
        finally { visitorGate.Release(); }
    }
    private async Task LoadGuestAsync(CancellationToken ct)
    {
        if (guestLoaded) return;
        guestLoaded = true;
        if (guestStore is null) return;
        try
        {
            // Only the old anonymous registration record is compatible.
            var saved = await guestStore.ReadAsync("guest-douyin", ct);
            var ticket = saved is { Length: <= 8192 } ? JsonSerializer.Deserialize<GuestTicket>(saved) : null;
            if (ticket is not null && ticket.Expires > DateTimeOffset.UtcNow && ticket.Expires <= DateTimeOffset.UtcNow.AddDays(1)
                && (ticket.Token is null || ValidVisitor(ticket.Token)))
            { visitor = ticket.Token; visitorExpires = ticket.Expires; guestRefreshedAt = ticket.RefreshedAt; }
        }
        catch (Exception ex) when (ex is JsonException or IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException) { }
    }
    private DouyinGuestVerificationException GuestVerificationRequired(bool requiresLogin = false)
    {
        browserRequiredUntil = DateTimeOffset.UtcNow.AddMinutes(15); browserLoginRequired = requiresLogin;
        return new(requiresLogin ? "Douyin yêu cầu đăng nhập." : "Douyin yêu cầu xác minh trình duyệt/người dùng.", requiresLogin);
    }
    private static bool ValidVisitor(string value) => value.Length is > 0 and <= 4096
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '%' or '|');
    private async Task<JsonNode?> JsonAsync(string url, bool mobile, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, DouyinHttp.Validate(url));
        request.Headers.UserAgent.ParseAdd(mobile ? MobileAgent : DouyinHttp.Agent);
        request.Headers.Referrer = new Uri("https://www.douyin.com/"); request.Headers.Accept.ParseAdd("application/json");
        if (!mobile && visitor is not null) request.Headers.TryAddWithoutValidation("Cookie", "ttwid=" + visitor);
        using var response = await SendAsync(request, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(8));
            await using var stream = await response.Content.ReadAsStreamAsync(budget.Token); using var body = new MemoryStream();
            var buffer = new byte[8192]; int n;
            while ((n = await stream.ReadAsync(buffer, budget.Token)) > 0)
            { if (body.Length + n > 4 * 1024 * 1024) return null; body.Write(buffer, 0, n); }
            if (body.Length == 0) { if (!mobile) webReceivedEmpty = true; return null; }
            var bytes = body.ToArray();
            // Some gateways return HTTP 200 with a verification page instead of JSON.
            // A normal empty/HTML page still does not authorize account fallback.
            var prefix = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 16384));
            if ((prefix.TrimStart().StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
                || prefix.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                && (prefix.Contains("请完成验证", StringComparison.Ordinal) || prefix.Contains("验证中心", StringComparison.Ordinal)
                    || prefix.Contains("<iframe", StringComparison.OrdinalIgnoreCase) && prefix.Contains("captcha", StringComparison.OrdinalIgnoreCase)))
                throw GuestVerificationRequired();
            var json = JsonNode.Parse(bytes);
            if (json?["verify_url"] is not null || json?["captcha"] is not null) throw GuestVerificationRequired();
            if (json?["login_required"] is JsonValue login && login.TryGetValue<bool>(out var required) && required) throw GuestVerificationRequired(true);
            return json;
        }
        catch (System.Text.Json.JsonException) { return null; }
        catch (Exception ex) when (ex is IOException or HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }
    private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            EnsureAllowed(); var delay = nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(8));
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            { Pause(response.Headers.RetryAfter); response.Dispose(); EnsureAllowed(); }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // Missing browser identity is a web-gateway limitation, not proof the account is banned.
                var prefix = new byte[512]; await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
                var n = await stream.ReadAsync(prefix, budget.Token);
                var rejection = Encoding.UTF8.GetString(prefix, 0, n);
                var identityMissing = request.RequestUri!.Host == "www.douyin.com" && rejection.Contains("Uifid Not Found", StringComparison.OrdinalIgnoreCase);
                var loginRequired = response.StatusCode == HttpStatusCode.Unauthorized;
                var verification = rejection.Contains("verification", StringComparison.OrdinalIgnoreCase) || rejection.Contains("captcha", StringComparison.OrdinalIgnoreCase);
                response.Dispose();
                if (identityMissing) { webUnavailableUntil = DateTimeOffset.UtcNow.AddMinutes(30); return null; }
                if (loginRequired || verification) throw GuestVerificationRequired(loginRequired);
                return null;
            }
            return response;
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        finally { nextRequest = DateTimeOffset.UtcNow + spacing; gate.Release(); }
    }
    private void Pause(System.Net.Http.Headers.RetryConditionHeaderValue? retry)
    {
        var requested = retry?.Date ?? DateTimeOffset.UtcNow + (retry?.Delta ?? TimeSpan.Zero);
        blockedUntil = requested > DateTimeOffset.UtcNow.AddMinutes(15) ? requested : DateTimeOffset.UtcNow.AddMinutes(15);
    }
    public void Dispose() { if (ownsClient) http.Dispose(); gate.Dispose(); visitorGate.Dispose(); }
}
