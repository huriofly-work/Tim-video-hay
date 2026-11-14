using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class YouTubeDestination(GoogleTransport api, ISecretStore secrets, bool checkTitles = true) : IYouTubeDestination
{
    private const string BaseUrl = "https://www.googleapis.com/youtube/v3/";
    private const int ChunkSize = 8 * 1024 * 1024; // Multiple of 256 KiB, including across resumed sessions.
    public async Task CheckDuplicateAsync(VideoJob job, CancellationToken ct)
    {
        if (!checkTitles || job.YouTubeId is not null || job.MediaSendStarted && job.UploadSessionKey is not null) return;
        var target = job.Channel!;
        var channel = await api.JsonAsync(target.CredentialId, HttpMethod.Get,
            BaseUrl + "channels?part=id,contentDetails&mine=true&maxResults=50", null, ct);
        var channels = channel["items"] as JsonArray;
        if (channels?.Count != 1 || channels[0]?["id"]?.ToString() != target.ChannelId)
            throw new NeedsActionException("Không xác định được đúng kênh để kiểm tra tiêu đề trùng. Đã dừng trước tải/đăng.");
        var playlist = channels[0]?["contentDetails"]?["relatedPlaylists"]?["uploads"]?.ToString();
        if (string.IsNullOrWhiteSpace(playlist)) throw new NeedsActionException("YouTube không trả danh sách video đã đăng. Không thể kiểm tra trùng nên chưa đăng.");
        string? page = null; var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var result = await api.JsonAsync(target.CredentialId, HttpMethod.Get, BaseUrl + "playlistItems?part=snippet,contentDetails&maxResults=50&playlistId="
                + Uri.EscapeDataString(playlist) + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)), null, ct);
            if (result["items"] is not JsonArray items) throw new NeedsActionException("Danh sách video YouTube không hợp lệ. Chưa xác minh được trùng tiêu đề.");
            if (items.Any(x => string.IsNullOrWhiteSpace(x?["contentDetails"]?["videoId"]?.ToString() ?? x?["snippet"]?["resourceId"]?["videoId"]?.ToString())))
                throw new NeedsActionException("Danh sách YouTube thiếu ID video. Không thể kiểm tra hết tiêu đề nên app chưa đăng.");
            // The owner's uploads playlist includes private/unlisted videos; avoid search's partial results.
            var ids = items.Select(x => x?["contentDetails"]?["videoId"]?.ToString() ?? x?["snippet"]?["resourceId"]?["videoId"]?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().ToArray();
            if (ids.Length > 0)
            {
                var videos = await api.JsonAsync(target.CredentialId, HttpMethod.Get, BaseUrl + "videos?part=snippet&id=" + Uri.EscapeDataString(string.Join(',', ids)), null, ct);
                if (videos["items"] is not JsonArray entries) throw new NeedsActionException("Không đọc được tiêu đề video YouTube. App đã dừng.");
                if (entries.Count != ids.Length || !entries.Select(v => v?["id"]?.ToString() ?? "").ToHashSet(StringComparer.Ordinal).SetEquals(ids))
                    throw new NeedsActionException("YouTube chưa trả đủ các video trong danh sách đã đăng. Chưa kiểm tra hết tiêu đề nên app đã dừng, không tạo upload mới.");
                foreach (var video in entries)
                {
                    if (!ids.Contains(video?["id"]?.ToString()) || video?["snippet"]?["channelId"]?.ToString() != target.ChannelId)
                        throw new NeedsActionException("Danh sách video không khớp kênh đã chọn. App đã dừng.");
                    if (string.IsNullOrWhiteSpace(video?["snippet"]?["title"]?.ToString()))
                        throw new NeedsActionException("Một video YouTube thiếu tiêu đề. Chưa kiểm tra được trùng nên app đã dừng.");
                    if (Rules.SameTitle(video?["snippet"]?["title"]?.ToString() ?? "", job.Title))
                        throw new NeedsActionException("Đã dừng: kênh YouTube đã có video trùng tiêu đề “" + job.Title + "”. Kiểm tra video hiện có hoặc chọn tên khác; app chưa đăng lại.");
                }
            }
            page = result["nextPageToken"]?.ToString();
            if (page is not null && (!seen.Add(page) || seen.Count > 2000)) throw new NeedsActionException("Không đọc hết danh sách YouTube để kiểm tra trùng. App đã dừng.");
        } while (page is not null);
    }
    public async Task<ChannelTarget> ChannelAsync(string credential, CancellationToken ct)
    {
        var result = await api.JsonAsync(credential, HttpMethod.Get, BaseUrl + "channels?part=id,snippet&mine=true&maxResults=50", null, ct);
        var channels = result["items"]!.AsArray();
        if (channels.Count != 1) throw new NeedsActionException("Quyền OAuth không xác định đúng một kênh. Kết nối lại và chọn tài khoản/kênh thương hiệu cần đăng.");
        return new(channels[0]!["id"]!.GetValue<string>(), channels[0]!["snippet"]!["title"]!.GetValue<string>(), credential);
    }
    public async Task PreflightAsync(ChannelTarget target, CancellationToken ct)
    {
        if ((await ChannelAsync(target.CredentialId, ct)).ChannelId != target.ChannelId)
            throw new NeedsActionException("Kênh được OAuth cấp quyền khác kênh lưu trong công việc. Đã dừng để không đăng nhầm.");
    }
    public async Task<bool> ExistsAsync(VideoJob job, CancellationToken ct)
    {
        var target = job.Channel ?? throw new NeedsActionException("Công việc không có kênh gốc.");
        if (job.YouTubeId is null) throw new NeedsActionException("Chưa có ID video YouTube để kiểm tra.");
        await PreflightAsync(target, ct);
        var video = await api.JsonAsync(target.CredentialId, HttpMethod.Get, BaseUrl + "videos?part=snippet&id=" + Uri.EscapeDataString(job.YouTubeId), null, ct);
        var items = video["items"] as JsonArray ?? throw new NeedsActionException("YouTube chưa trả danh sách hợp lệ.");
        if (items.Count == 0) return false; // Successful owner-channel lookup with no ID: caller treats this as manual deletion.
        if (items.Count != 1 || items[0]?["id"]?.ToString() != job.YouTubeId || items[0]?["snippet"]?["channelId"]?.ToString() != target.ChannelId)
            throw new NeedsActionException("Video không khớp ID/kênh gốc.");
        return true;
    }
    public async Task DeleteVideoAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        if (job.YouTubeDeleted) return;
        if (job.YouTubeId is null) throw new NeedsActionException("Upload chưa có ID rõ ràng. Kiểm tra YouTube Studio trước khi xóa; app không đoán video cần xóa.");
        var target = job.Channel ?? throw new NeedsActionException("Không có kênh gốc để xóa video.");
        await PreflightAsync(target, ct);
        var video = await api.JsonAsync(target.CredentialId, HttpMethod.Get, BaseUrl + "videos?part=snippet&id=" + Uri.EscapeDataString(job.YouTubeId), null, ct);
        var items = video["items"] as JsonArray ?? throw new NeedsActionException("YouTube chưa trả danh sách hợp lệ; giữ kết quả cũ.");
        if (items.Count == 0) { job.YouTubeDeleted = true; await checkpoint(); return; }
        if (items.Count > 0)
        {
            if (items.Count != 1 || items[0]?["id"]?.GetValue<string>() != job.YouTubeId || items[0]?["snippet"]?["channelId"]?.GetValue<string>() != target.ChannelId)
                throw new NeedsActionException("Video không khớp ID/kênh gốc. App chưa gửi lệnh xóa.");
        }
        await api.RequireYouTubeDeletePermissionAsync(target.CredentialId, ct);
        job.YouTubeDeleteStarted = true; await checkpoint();
        using var request = new HttpRequestMessage(HttpMethod.Delete, BaseUrl + "videos?id=" + Uri.EscapeDataString(job.YouTubeId));
        using var response = await api.SendAsync(target.CredentialId, request, ct);
        if (response.StatusCode != HttpStatusCode.NotFound) await GoogleTransport.EnsureSuccessAsync(response, ct);
        job.YouTubeDeleted = true; await checkpoint();
    }
    public async Task UploadAndVerifyAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct)
    {
        var file = job.Media!;
        var credential = job.Channel!.CredentialId;
        job.ProgressAction = JobActions.UploadYouTube;
        void Report() { job.Progress = file is { Length: > 0 } ? Math.Clamp(100d * job.UploadedBytes / file.Length, 0, 100) : job.YouTubeId is not null ? 100 : 0; progress.Report(job.Progress); }
        Report();
        if (job.YouTubeId is null)
        {
            string? session = job.UploadSessionKey is null ? null : await secrets.ReadAsync(job.UploadSessionKey, ct);
            if (job.UploadSessionKey is not null && session is null && job.MediaSendStarted)
                throw new NeedsActionException("Mất phiên upload khi đã gửi video. Kiểm tra YouTube Studio; không tự tạo upload mới.");
            if (session is not null)
            {
                var offset = await QueryAsync(job, session, checkpoint, ct);
                Report();
                if (offset is null && !job.MediaSendStarted) session = null;
            }
            if (session is null && job.YouTubeId is null)
            {
                if (job.MediaSendStarted) throw new NeedsActionException("Phiên upload không còn hợp lệ. Đối soát YouTube Studio trước khi cho phép đăng lại.");
                await CheckDuplicateAsync(job, ct);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status");
                request.Content = new StringContent(JsonSerializer.Serialize(new {
                    snippet = new { title = job.Title, description = job.Description, categoryId = "22" },
                    status = new { privacyStatus = job.Privacy, selfDeclaredMadeForKids = false }
                }), Encoding.UTF8, "application/json");
                request.Headers.Add("X-Upload-Content-Length", file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                request.Headers.Add("X-Upload-Content-Type", file.MimeType);
                using var response = await api.SendAsync(credential, request, ct);
                await GoogleTransport.EnsureSuccessAsync(response, ct);
                var location = response.Headers.Location ?? throw new NeedsActionException("Google không trả phiên upload.");
                GoogleTransport.ValidateGoogleUri(location);
                session = location.AbsoluteUri;
                job.UploadSessionKey = "upload-" + job.Id + (job.YouTubeAttemptId is null ? "" : "-" + job.YouTubeAttemptId);
                await secrets.WriteAsync(job.UploadSessionKey, session, ct);
                job.UploadedBytes = 0; Report(); await checkpoint();
            }
            if (job.YouTubeId is null)
            {
                await using var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, true);
                var buffer = new byte[ChunkSize];
                while (job.YouTubeId is null && job.UploadedBytes < file.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    input.Position = job.UploadedBytes;
                    var length = (int)Math.Min(ChunkSize, file.Length - input.Position);
                    await input.ReadExactlyAsync(buffer.AsMemory(0, length), ct);
                    job.MediaSendStarted = true; await checkpoint();
                    using var request = new HttpRequestMessage(HttpMethod.Put, session);
                    request.Content = new ByteArrayContent(buffer, 0, length);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);
                    request.Content.Headers.ContentRange = new ContentRangeHeaderValue(job.UploadedBytes, job.UploadedBytes + length - 1, file.Length);
                    using var response = await api.SendAsync(credential, request, ct);
                    if ((int)response.StatusCode == 308)
                    {
                        var accepted = AcceptedBytes(response, file.Length);
                        if (accepted <= job.UploadedBytes || accepted > job.UploadedBytes + length) throw new NeedsActionException("Google trả tiến độ không hợp lệ. Tiếp tục để truy vấn lại phiên upload.");
                        job.UploadedBytes = accepted; Report(); await checkpoint();
                    }
                    else if (response.IsSuccessStatusCode)
                    {
                        await CompletedAsync(job, response, checkpoint, ct);
                    }
                    else if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                        throw new NeedsActionException("Phiên upload hết hạn sau khi gửi dữ liệu. Kiểm tra YouTube Studio trước khi tạo lại.");
                    else await GoogleTransport.EnsureSuccessAsync(response, ct);
                    Report();
                }
                if (job.YouTubeId is null) await QueryAsync(job, session!, checkpoint, ct);
                if (job.YouTubeId is null) throw new NeedsActionException("Đã gửi dữ liệu nhưng Google chưa trả ID video. Tiếp tục để truy vấn phiên; không tạo video mới.");
            }
        }
        await VerifyAsync(job, checkpoint, ct);
    }
    public async Task<bool> InspectPendingUploadAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        if (job.YouTubeId is not null) return true;
        if (job.Media is null || job.UploadSessionKey is null) return false;
        var session = await secrets.ReadAsync(job.UploadSessionKey, ct);
        if (session is null) return false;
        try
        {
            var offset = await QueryAsync(job, session, checkpoint, ct);
            return job.YouTubeId is not null || offset is not null && offset < job.Media.Length;
        }
        catch (NeedsActionException ex) when (ex.Kind == JobIssueKind.UnknownResult) { return false; }
    }
    private async Task<long?> QueryAsync(VideoJob job, string session, Func<Task> checkpoint, CancellationToken ct)
    {
        GoogleTransport.ValidateGoogleUri(new Uri(session));
        using var request = new HttpRequestMessage(HttpMethod.Put, session);
        request.Content = new ByteArrayContent([]);
        request.Content.Headers.ContentRange = new ContentRangeHeaderValue(job.Media!.Length);
        using var response = await api.SendAsync(job.Channel!.CredentialId, request, ct);
        if ((int)response.StatusCode == 308)
        {
            job.UploadedBytes = AcceptedBytes(response, job.Media.Length);
            job.Progress = Math.Clamp(100d * job.UploadedBytes / job.Media.Length, 0, 100);
            await checkpoint(); return job.UploadedBytes;
        }
        if (response.IsSuccessStatusCode) { await CompletedAsync(job, response, checkpoint, ct); return job.Media.Length; }
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            if (job.MediaSendStarted) throw new NeedsActionException("Phiên upload hết hạn và kết quả chưa rõ. Kiểm tra Studio; không tự tạo upload mới.", JobIssueKind.UnknownResult);
            return null;
        }
        await GoogleTransport.EnsureSuccessAsync(response, ct); return null;
    }
    public static long AcceptedBytes(HttpResponseMessage response, long total)
    {
        if (!response.Headers.TryGetValues("Range", out var values)) return 0;
        var match = System.Text.RegularExpressions.Regex.Match(values.Single(), @"^bytes=0-(\d+)$");
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out var end) || end < 0 || end >= total)
            throw new NeedsActionException("Phản hồi Range upload không hợp lệ.");
        return end + 1;
    }
    private static async Task CompletedAsync(VideoJob job, HttpResponseMessage response, Func<Task> checkpoint, CancellationToken ct)
    {
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var id = json?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id) || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
            throw new NeedsActionException("Upload trả thành công nhưng thiếu ID video. Giữ phiên và đối soát, không đăng lại.", JobIssueKind.UnknownResult);
        job.YouTubeId = id; job.UploadedBytes = job.Media!.Length; job.Progress = 100; await checkpoint();
    }
    private async Task VerifyAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        job.Stage = "Xác nhận video đã đăng YouTube";
        job.YouTubeVerificationStartedAt ??= DateTimeOffset.UtcNow;
        await checkpoint();
        var result = await api.JsonAsync(job.Channel!.CredentialId, HttpMethod.Get,
            BaseUrl + "videos?part=snippet,status&id=" + Uri.EscapeDataString(job.YouTubeId!), null, ct);
        var videos = result["items"]!.AsArray();
        if (videos.Count == 1)
        {
            var video = videos[0]!;
            if (video["snippet"]?["channelId"]?.GetValue<string>() != job.Channel.ChannelId)
                throw new NeedsActionException("Video trả về không thuộc kênh đã chọn. Kiểm tra Studio.");
            var status = video["status"]!;
            if (status["uploadStatus"]?.GetValue<string>() is "failed" or "rejected" or "deleted")
                throw new NeedsActionException("YouTube từ chối hoặc xóa video. Kiểm tra Studio; ID đã được giữ.");
            var actual = status["privacyStatus"]?.GetValue<string>();
            if (actual is not null && actual != job.Privacy)
                throw new NeedsActionException($"Video đã tải lên nhưng chế độ thực tế là {actual}, yêu cầu là {job.Privacy}. Kiểm tra chế độ trên YouTube Studio. Đã giữ ID video; không đăng lại.");
            if (status["selfDeclaredMadeForKids"]?.GetValue<bool>() == true || status["madeForKids"]?.GetValue<bool>() == true)
                throw new NeedsActionException("Video đã tải lên nhưng chưa xác minh được 'không dành cho trẻ em'. Kiểm tra YouTube Studio; không đăng lại.");
            if (actual == job.Privacy
                && status["selfDeclaredMadeForKids"]?.GetValue<bool>() == false && status["madeForKids"]?.GetValue<bool>() == false) return;
        }
        else if (DateTimeOffset.UtcNow - job.YouTubeVerificationStartedAt > TimeSpan.FromHours(24))
            throw new NeedsActionException("Không tìm thấy video đã tải lên sau 24 giờ. Kiểm tra đúng kênh trên Studio; app giữ ID và không đăng lại.");
        throw new YouTubePendingException("Video đã tải lên; YouTube chưa trả đủ thông tin kênh, chế độ và đối tượng để xác nhận.");
    }
}
