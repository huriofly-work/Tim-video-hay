using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed partial class MediaDownload(HttpClient http, ISecretStore secrets, TimeSpan? inactivityLimit = null, TimeSpan? slowTransferWindow = null)
{
    private readonly TimeSpan inactivity = inactivityLimit ?? TimeSpan.FromSeconds(60);
    private readonly TimeSpan slowWindow = slowTransferWindow ?? TimeSpan.FromSeconds(10);
    private const long ChunkSize = 32 * 1024 * 1024;
    private const int ParallelChunkSize = 4 * 1024 * 1024;
    private const long SlowBytesPerSecond = 384 * 1024;
    private sealed class ParallelRangeUnavailableException : Exception;
    private sealed class SlowDownloadException : Exception;
    private sealed class TransferState
    {
        public int Connections = 4;
        public int SlowReconnects;
    }
    private sealed record Receipt(string SourceId, string FormatId, string Path, string Validator, long Length, SegmentMap? Segments = null);
    private static string ReceiptKey(VideoJob job) => "download-" + job.Id + (job.DownloadAttemptId is null ? "" : "-" + job.DownloadAttemptId);
    private static bool Matches(Receipt? receipt, VideoJob job, string path) => receipt is { Length: > 0 } && receipt.Length <= 274_877_906_944
        && receipt.Path == path && receipt.SourceId == job.Source?.Id && receipt.FormatId == job.DownloadFormatId
        && (receipt.Segments is null ? !string.IsNullOrEmpty(receipt.Validator) : ValidMap(receipt));
    public static async Task RestoreProgressAsync(VideoJob job, string destination, ISecretStore secrets)
    {
        PrivateFiles.RejectLinks(destination + ".part");
        Receipt? receipt = null;
        try { var value = await secrets.ReadAsync(ReceiptKey(job)); if (value is not null) receipt = JsonSerializer.Deserialize<Receipt>(value); }
        catch (JsonException) { }
        if (!Matches(receipt, job, destination) || !File.Exists(destination + ".part")) return;
        var bytes = SavedBytes(receipt!, new FileInfo(destination + ".part").Length);
        if (bytes < 0) return;
        job.ProgressAction = JobActions.Download; job.DownloadedBytes = bytes; job.DownloadTotalBytes = receipt!.Length;
        job.Progress = job.DisplayProgress;
    }
    public async Task DownloadAsync(VideoJob job, string url, string destination, IProgress<double> progress, CancellationToken ct, Func<Task>? checkpoint = null)
    {
        _ = DouyinHttp.Validate(url);
        PrivateFiles.RejectLinks(Path.GetDirectoryName(destination)!, recursive: true);
        void Report(long bytes, long total)
        {
            job.ProgressAction = JobActions.Download; job.DownloadedBytes = bytes; job.DownloadTotalBytes = total;
            job.Progress = total > 0 ? Math.Clamp(100d * bytes / total, 0, 100) : 0;
            progress.Report(job.Progress);
        }
        try
        {
            if (File.Exists(destination)) { var size = new FileInfo(destination).Length; Report(size, size); return; } // Caller validates the complete owned file.
            var partial = destination + ".part";
            var key = ReceiptKey(job);
            Receipt? receipt = null;
            try { var value = await secrets.ReadAsync(key, ct); if (value is not null) receipt = JsonSerializer.Deserialize<Receipt>(value); }
            catch (JsonException) { }
            var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (!Matches(receipt, job, destination) || SavedBytes(receipt!, offset) < 0
                || receipt!.Segments is not null && !File.Exists(partial))
            { offset = 0; receipt = null; } // Legacy/unverifiable partials restart, never mix media versions.
            Report(receipt is null ? 0 : SavedBytes(receipt, offset), receipt?.Length ?? 0);
            if (checkpoint is not null) await checkpoint();
            var transfer = new TransferState();
            if (receipt?.Segments is not null)
            {
                receipt = await DownloadSegmentsAsync(job, url, destination, receipt, transfer, Report, checkpoint, ct);
                if (receipt is null) return;
                offset = new FileInfo(partial).Length;
                transfer.Connections = 1;
            }
            if (receipt is not null && offset == receipt.Length && File.Exists(partial))
            {
                ct.ThrowIfCancellationRequested();
                PrivateFiles.RejectLinks(destination); File.Move(partial, destination, false);
                await secrets.DeleteAsync(key, ct); return;
            }
            if (receipt is null)
            {
                // Some Douyin CDNs omit ETag/Last-Modified on 206. HEAD obtains the
                // validator without starting a large response body or waiting on it.
                using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                headersTimeout.CancelAfter(inactivity);
                using var head = await DouyinHttp.GetAsync(http, url, job.Source!.CanonicalUrl, headersTimeout.Token, head: true);
                CheckAccess(head);
                using var fallback = head.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented || head.Content.Headers.ContentLength is null
                    ? await DouyinHttp.GetAsync(http, url, job.Source!.CanonicalUrl, headersTimeout.Token) : null;
                var headers = fallback ?? head;
                CheckAccess(headers);
                headers.EnsureSuccessStatusCode();
                if (headers.Content.Headers.ContentLength is long total && total > 0 && total <= 274_877_906_944)
                    receipt = new(job.Source!.Id, job.DownloadFormatId!, destination, Validator(headers), total);
            }
            var failures = 0;
            var parallelReady = false;
            var lastCheckpoint = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (parallelReady && transfer.Connections > 1 && receipt is not null && StrongTag(receipt.Validator)
                    && receipt.Length - offset >= 2L * ParallelChunkSize)
                {
                    receipt = await DownloadSegmentsAsync(job, url, destination, receipt, transfer, Report, checkpoint, ct);
                    if (receipt is null) return;
                    offset = new FileInfo(partial).Length;
                    transfer.Connections = 1;
                    continue;
                }
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(inactivity);
                    var segmentSize = transfer.Connections > 1 && receipt is not null && StrongTag(receipt.Validator)
                        && receipt.Length - offset >= 2L * ParallelChunkSize ? ParallelChunkSize : ChunkSize;
                    var end = offset + segmentSize - 1;
                    if (receipt is { Length: > 0 }) end = Math.Min(end, receipt.Length - 1);
                    using var response = await DouyinHttp.GetAsync(http, url, job.Source!.CanonicalUrl, timeout.Token,
                        receipt is not null && receipt.Validator.Length == 0 ? null : new RangeHeaderValue(offset, end),
                        receipt is { Validator.Length: > 0 } ? receipt.Validator : null, closeConnection: transfer.SlowReconnects > 0);
                    if (offset > 0 && response.StatusCode == HttpStatusCode.OK) offset = 0; // Server no longer accepts resume; replace partial from byte zero.
                    CheckAccess(response);
                    response.EnsureSuccessStatusCode();
                    var range = response.Content.Headers.ContentRange;
                    var ranged = response.StatusCode == HttpStatusCode.PartialContent;
                    var length = ranged ? range?.Length : response.Content.Headers.ContentLength;
                    if (length is null or <= 0 or > 274_877_906_944) throw new NeedsActionException("Máy chủ chưa xác nhận dung lượng video hợp lệ. Thử lại sau.");
                    if (ranged && (range?.Unit != "bytes" || range.From != offset || range.To is null || range.To < offset || range.To >= length || range.To > end))
                        throw new NeedsActionException("Máy chủ trả dải dữ liệu video không hợp lệ. App đã dừng để không nối sai file.");
                    var expected = ranged ? range!.To!.Value - offset + 1 : length.Value;
                    if (response.Content.Headers.ContentLength is long count && count != expected)
                        throw new NeedsActionException("Dung lượng phản hồi video không khớp dải dữ liệu. App chưa ghi thêm.");
                    var validator = Validator(response);
                    // A compliant server answers If-Range with 206 only when its
                    // validator matches, and need not repeat ETag in that response.
                    if (ranged && receipt is { Validator.Length: > 0 }
                        && (validator.Length == 0 || StrongTag(receipt.Validator) && response.Headers.ETag is null)) validator = receipt.Validator;
                    if (offset > 0 && (receipt!.Length != length || receipt.Validator != validator))
                        throw new NeedsActionException("Bản video trên máy chủ đã thay đổi. App chưa nối dữ liệu vào file cũ.");
                    PrivateFiles.RejectLinks(partial);
                    if (offset == 0 && File.Exists(partial)) File.Delete(partial); // Remove old bytes before publishing a new validator.
                    receipt = new(job.Source.Id, job.DownloadFormatId!, destination, validator, length.Value);
                    await secrets.WriteAsync(key, JsonSerializer.Serialize(receipt), ct);
                    Report(offset, length.Value);
                    if (checkpoint is not null) await checkpoint();
                    lastCheckpoint.Restart();
                    long received = 0;
                    var transferTime = System.Diagnostics.Stopwatch.StartNew();
                    await using (var output = new FileStream(partial, offset == 0 ? FileMode.CreateNew : FileMode.Open, FileAccess.Write, FileShare.None, 131072, true))
                    {
                        if (output.Length != offset) throw new IOException("Partial changed");
                        output.Position = offset;
                        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                        var buffer = new byte[131072];
                        while (true)
                        {
                            // Bound inactivity, not the total transfer time. Some CDNs
                            // refuse Range and stream the entire high-quality file.
                            timeout.CancelAfter(inactivity);
                            var read = await input.ReadAsync(buffer, timeout.Token);
                            if (read == 0) break;
                            if (received + read > expected) throw new IOException("Response exceeds declared range");
                            await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token); received += read;
                            Report(output.Position, length.Value);
                            if (checkpoint is not null && lastCheckpoint.Elapsed >= TimeSpan.FromSeconds(5))
                            {
                                await output.FlushAsync(ct);
                                await checkpoint(); lastCheckpoint.Restart();
                            }
                            // An incomplete response is closed before requesting the next
                            // range. Only resumable, validated transfers may reconnect.
                            if (ranged && StrongTag(validator) && transfer.SlowReconnects < 2 && received < expected
                                && transferTime.Elapsed >= slowWindow
                                && received / transferTime.Elapsed.TotalSeconds < SlowBytesPerSecond)
                                throw new SlowDownloadException();
                        }
                        await output.FlushAsync(ct); output.Flush(true); offset = output.Length;
                    }
                    if (received != expected) throw new IOException("Incomplete range");
                    if (offset == length)
                    {
                        PrivateFiles.RejectLinks(destination); File.Move(partial, destination, false);
                        await secrets.DeleteAsync(key, ct); return;
                    }
                    failures = 0;
                    parallelReady = ranged && StrongTag(validator);
                    if (string.IsNullOrEmpty(validator)) throw new NeedsActionException("Máy chủ không cung cấp dữ liệu xác minh để nối các phần video. Thử lại sau.");
                }
                catch (SlowDownloadException)
                {
                    transfer.SlowReconnects++;
                    PrivateFiles.RejectLinks(partial);
                    offset = new FileInfo(partial).Length;
                    Report(offset, receipt!.Length);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or IOException or OperationCanceledException)
                {
                    transfer.Connections = Math.Max(1, transfer.Connections / 2);
                    if (++failures >= 3) throw new NeedsActionException("Kết nối tải video bị gián đoạn sau ba lần thử. Đã giữ phần tải dở; bấm Tiếp tục để thử lại.");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, failures)), ct);
                    PrivateFiles.RejectLinks(partial);
                    offset = receipt is not null && !string.IsNullOrEmpty(receipt.Validator) && File.Exists(partial) ? new FileInfo(partial).Length : 0;
                    if (receipt is not null && offset >= receipt.Length) offset = 0;
                    Report(offset, receipt?.Length ?? 0);
                }
            }
        }
        finally { if (checkpoint is not null) await checkpoint(); } // Includes pause/failure, after the partial file has closed/flushed.
    }
    private static bool StrongTag(string validator) => EntityTagHeaderValue.TryParse(validator, out var tag) && !tag.IsWeak && tag.Tag != "*";
    private static string Validator(HttpResponseMessage response) => response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : response.Content.Headers.LastModified?.ToString("R") ?? "";
    private static void CheckAccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new RetryLaterException("CDN giới hạn truy cập (HTTP 429). Đã giữ phần tải dở; chờ thời gian cho phép trước khi thử lại.", response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5)), "douyin-cdn");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Gone)
            throw new DouyinMediaUrlExpiredException();
    }
}
