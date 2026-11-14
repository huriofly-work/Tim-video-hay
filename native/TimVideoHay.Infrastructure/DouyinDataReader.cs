using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

// Required video metadata: public HTTP, anonymous browser, then account browser.
// Missing optional posters never trigger a different browser/session.
public sealed class DouyinDataReader(DouyinPublicReader publicReader, IDouyinVideoBrowserReader guestVideo,
    IDouyinBrowserReader guestChannel, IDouyinVideoBrowserReader accountVideo, IDouyinBrowserReader accountChannel, Action<string>? notify = null) : IDouyinVideoBrowserReader, IDouyinBrowserReader
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public void Forget(SourceVideo source) { publicReader.Forget(source); guestVideo.Forget(source); accountVideo.Forget(source); }
    public async Task<JsonNode> ReadVideoMetadataAsync(SourceVideo source, CancellationToken ct, bool readCovers = true)
    {
        await gate.WaitAsync(ct);
        try
        {
            publicReader.EnsureAllowed();
            JsonNode? info; DouyinGuestVerificationException? interaction = null;
            try { info = await publicReader.ReadAsync(source, readCovers, ct); }
            catch (DouyinGuestVerificationException ex) { info = null; interaction = ex; }
            if (Valid(info, source, readCovers)) return info!;
            if (interaction?.RequiresLogin != true)
            {
                publicReader.EnsureAllowed();
                notify?.Invoke("Nguồn HTTP chưa đủ dữ liệu đúng video. App tự thử phiên khách Douyin; chỉ hiện cửa sổ khi cần xác minh.");
                try { info = await guestVideo.ReadVideoMetadataAsync(source, ct, readCovers); }
                catch (DouyinTerminalException ex) { if (ex.Blocked) publicReader.SuspendForChallenge(ex.RetryAt); throw; }
                catch (NeedsActionException ex) when (ex.Kind is JobIssueKind.General or JobIssueKind.SourceUnavailable) { info = null; }
                if (Valid(info, source, readCovers))
                {
                    publicReader.Remember(source, info!, qualityChecked: readCovers, coversChecked: readCovers); return info!;
                }
            }
            // A bounded guest read did not provide required metadata, or the server
            // explicitly requested login. Continue automatically; never replay a
            // terminal/rate-limit/user-cancellation error using another identity.
            publicReader.EnsureAllowed(); ct.ThrowIfCancellationRequested();
            notify?.Invoke(interaction?.RequiresLogin == true
                ? "Douyin yêu cầu đăng nhập để đọc video. App tự dùng phiên đã đăng nhập; chỉ hiện cửa sổ khi cần bạn thao tác."
                : "Phiên khách chưa đủ dữ liệu video. App tự dùng phiên Douyin đã đăng nhập để đọc đúng video và tiếp tục từ tiến độ đã lưu.");
            try { info = await accountVideo.ReadVideoMetadataAsync(source, ct, readCovers); }
            catch (DouyinTerminalException ex) { if (ex.Blocked) publicReader.SuspendForChallenge(ex.RetryAt); throw; }
            if (!Valid(info, source, readCovers)) throw new NeedsActionException("Douyin chưa cung cấp dữ liệu hợp lệ của đúng video sau khi thử nguồn công khai, khách và tài khoản. Công việc và tiến độ được giữ.", JobIssueKind.SourceUnavailable);
            publicReader.Remember(source, info!, qualityChecked: readCovers, coversChecked: readCovers); return info!;
        }
        finally { gate.Release(); }
    }
    public async Task<DouyinChannelSnapshot> ReadAsync(SourceVideo source, string? secUid, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            publicReader.EnsureAllowed(); secUid ??= publicReader.Author(source);
            // The anonymous browser uses the site's own signed requests for profile/posts.
            // Do not run a second unsigned channel API engine that normally gets blocked.
            try { return await guestChannel.ReadAsync(source, secUid, ct); }
            catch (DouyinTerminalException ex) { if (ex.Blocked) publicReader.SuspendForChallenge(ex.RetryAt); throw; }
            catch (NeedsActionException ex) when (ex is not DouyinTerminalException) { publicReader.EnsureAllowed(); }
            notify?.Invoke("Phiên khách chưa đủ dữ liệu kênh nguồn. App tự dùng phiên Douyin đã đăng nhập để đọc phần còn thiếu.");
            try { return await accountChannel.ReadAsync(source, secUid, ct); }
            catch (DouyinTerminalException ex) { if (ex.Blocked) publicReader.SuspendForChallenge(ex.RetryAt); throw; }
        }
        finally { gate.Release(); }
    }
    private static bool Valid(JsonNode? info, SourceVideo source, bool download) => info?["id"]?.ToString() == source.Id
        && (download ? info["formats"] is JsonArray formats && formats.Any(f => f is not null && DownloadPolicy.Usable(f) && DouyinHttp.Allowed(f["url"]?.ToString() ?? ""))
            : info["source_duration_ms"] is JsonValue v && v.TryGetValue<long>(out var ms) && ms > 0);
}
