using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed partial class MediaDownload
{
    // End is exclusive. File length is NOT downloaded progress when gaps exist.
    private sealed record DownloadedRange(long Start, long End);
    private sealed record SegmentMap(string Validator, DownloadedRange[] Ranges, int Version = 1);
    private static bool ValidMap(Receipt receipt)
    {
        if (receipt.Segments is not { Version: 1, Ranges: not null } map || !StrongTag(map.Validator)
            || map.Ranges.Length > 65536 || receipt.Validator != "") return false;
        long end = 0;
        foreach (var range in map.Ranges)
        {
            if (range is null || range.Start < end || range.Start < 0 || range.End <= range.Start || range.End > receipt.Length) return false;
            end = range.End;
        }
        return true;
    }
    private static long SavedBytes(Receipt receipt, long fileLength)
    {
        if (fileLength > receipt.Length) return -1;
        if (receipt.Segments is null) return fileLength;
        if (!ValidMap(receipt) || receipt.Segments.Ranges.Any(r => r.End > fileLength)) return -1;
        return receipt.Segments.Ranges.Sum(r => r.End - r.Start);
    }

    // Null means complete. A legacy receipt means Range was refused: retain only
    // the contiguous prefix and let the sequential downloader validate/restart it.
    private async Task<Receipt?> DownloadSegmentsAsync(VideoJob job, string url, string destination, Receipt receipt, TransferState transfer,
        Action<long, long> report, Func<Task>? checkpoint, CancellationToken ct)
    {
        var partial = destination + ".part";
        var key = ReceiptKey(job);
        var validator = receipt.Segments?.Validator ?? receipt.Validator;
        PrivateFiles.RejectLinks(partial);
        var ranges = receipt.Segments?.Ranges.ToList() ?? [new(0, new FileInfo(partial).Length)];
        ranges.RemoveAll(r => r.Start == r.End);
        var failures = 0;
        var sequential = false;
        var savedAt = System.Diagnostics.Stopwatch.StartNew();
        using var writes = new SemaphoreSlim(1, 1);
        await using (var output = new FileStream(partial, FileMode.Open, FileAccess.Write, FileShare.None, 131072, true))
        {
            if (SavedBytes(receipt, output.Length) < 0) throw new IOException("Partial changed");
            // An empty legacy Validator makes older app versions restart safely,
            // rather than mistaking the length of a file with gaps for a prefix.
            await PersistAsync();
            try
            {
                while (ranges.Sum(r => r.End - r.Start) < receipt.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    var pending = new Queue<DownloadedRange>();
                    long next = 0;
                    foreach (var range in ranges) { EnqueueGap(next, range.Start); next = range.End; }
                    EnqueueGap(next, receipt.Length);
                    using var wave = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
                    var workers = Enumerable.Range(0, Math.Min(transfer.Connections, pending.Count)).Select(_ => WorkerAsync()).ToArray();
                    try { await Task.WhenAll(workers); }
                    catch
                    {
                        ct.ThrowIfCancellationRequested();
                        var failure = errors.FirstOrDefault(e => e is RetryLaterException)
                            ?? errors.FirstOrDefault(e => e is DouyinMediaUrlExpiredException)
                            ?? errors.FirstOrDefault(e => e is NeedsActionException)
                            ?? errors.FirstOrDefault(e => e is ParallelRangeUnavailableException)
                            ?? errors.FirstOrDefault(e => e is SlowDownloadException)
                            ?? errors.FirstOrDefault(e => e is not OperationCanceledException)
                            ?? errors.First();
                        if (failure is ParallelRangeUnavailableException) { sequential = true; break; }
                        if (failure is SlowDownloadException) { transfer.SlowReconnects++; continue; }
                        if (failure is HttpRequestException or IOException or OperationCanceledException)
                        {
                            transfer.Connections = Math.Max(1, transfer.Connections / 2);
                            if (++failures >= 3) throw new NeedsActionException("Kết nối tải video bị gián đoạn sau ba lần thử. Đã giữ phần tải dở; bấm Tiếp tục để thử lại.");
                            await PersistAsync();
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, failures)), ct);
                            continue;
                        }
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                        throw;
                    }

                    void EnqueueGap(long start, long end)
                    {
                        for (; start < end; start += ParallelChunkSize)
                            pending.Enqueue(new(start, Math.Min(end, start + ParallelChunkSize)));
                    }
                    async Task WorkerAsync()
                    {
                        var buffer = new byte[131072];
                        try
                        {
                            while (true)
                            {
                                wave.Token.ThrowIfCancellationRequested();
                                DownloadedRange part;
                                lock (pending) { if (!pending.TryDequeue(out part!)) return; }
                                await ReadPartAsync(part, buffer, wave.Token);
                            }
                        }
                        catch (Exception ex) { errors.Enqueue(ex); await wave.CancelAsync(); throw; }
                    }
                }
            }
            finally
            {
                // All workers have stopped. Pause/failure keeps even partially
                // received ranges; persist durable data before publishing progress.
                await PersistAsync();
            }
            if (sequential)
            {
                var prefix = ranges.FirstOrDefault() is { Start: 0 } first ? first.End : 0;
                output.SetLength(prefix); await output.FlushAsync(); output.Flush(true);
                receipt = receipt with { Validator = validator, Segments = null };
                await secrets.WriteAsync(key, JsonSerializer.Serialize(receipt));
                report(prefix, receipt.Length);
                return receipt;
            }

            async Task PersistAsync()
            {
                await output.FlushAsync(); output.Flush(true);
                receipt = receipt with { Validator = "", Segments = new(validator, ranges.ToArray()) };
                await secrets.WriteAsync(key, JsonSerializer.Serialize(receipt));
                savedAt.Restart();
            }
            async Task ReadPartAsync(DownloadedRange part, byte[] buffer, CancellationToken token)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(inactivity);
                using var response = await DouyinHttp.GetAsync(http, url, job.Source!.CanonicalUrl, timeout.Token,
                    new RangeHeaderValue(part.Start, part.End - 1), validator, closeConnection: transfer.SlowReconnects > 0);
                CheckAccess(response); response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.OK) throw new ParallelRangeUnavailableException();
                var range = response.Content.Headers.ContentRange;
                var tag = response.Headers.ETag?.ToString();
                if (response.StatusCode != HttpStatusCode.PartialContent || range?.Unit != "bytes"
                    || range.From != part.Start || range.Length != receipt.Length || range.To is null
                    || range.To < part.Start || range.To >= part.End || tag is not null && tag != validator)
                    throw new NeedsActionException("Dữ liệu tải song song không khớp bản video/dải byte đã xác nhận. Đã giữ phần tải trước đó.");
                if (range.To != part.End - 1) throw new ParallelRangeUnavailableException();
                if (response.Content.Headers.ContentLength is long length && length != part.End - part.Start)
                    throw new NeedsActionException("Dung lượng phần tải song song không khớp dải byte. App chưa nối dữ liệu.");
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                var position = part.Start;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (position < part.End)
                {
                    timeout.CancelAfter(inactivity);
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, part.End - position)), timeout.Token);
                    if (read == 0) throw new IOException("Incomplete parallel range");
                    await writes.WaitAsync(token);
                    try
                    {
                        output.Position = position;
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        AddRange(position, position + read); position += read;
                        report(ranges.Sum(r => r.End - r.Start), receipt.Length);
                        if (savedAt.Elapsed >= TimeSpan.FromSeconds(5))
                        {
                            await PersistAsync();
                            if (checkpoint is not null) await checkpoint();
                        }
                    }
                    finally { writes.Release(); }
                    if (transfer.SlowReconnects < 2 && position < part.End && elapsed.Elapsed >= slowWindow
                        && (position - part.Start) / elapsed.Elapsed.TotalSeconds < SlowBytesPerSecond)
                        throw new SlowDownloadException();
                }
                timeout.CancelAfter(inactivity);
                if (await input.ReadAsync(buffer.AsMemory(0, 1), timeout.Token) != 0)
                {
                    await writes.WaitAsync();
                    try
                    {
                        // The response exceeded its declared size. Do not retain it
                        // as a complete range, even if all expected bytes were read.
                        var remainder = ranges.SelectMany(r => new[] { new DownloadedRange(r.Start, Math.Min(r.End, part.Start)), new DownloadedRange(Math.Max(r.Start, part.End), r.End) })
                            .Where(r => r.Start < r.End).ToArray();
                        ranges.Clear(); ranges.AddRange(remainder);
                        report(ranges.Sum(r => r.End - r.Start), receipt.Length);
                    }
                    finally { writes.Release(); }
                    throw new NeedsActionException("Máy chủ trả thừa dữ liệu so với dải byte. Đã giữ các phần tải hợp lệ khác.");
                }
            }
            void AddRange(long start, long end)
            {
                var index = ranges.FindIndex(r => r.End >= start);
                if (index < 0) { ranges.Add(new(start, end)); return; }
                while (index < ranges.Count && ranges[index].Start <= end)
                {
                    start = Math.Min(start, ranges[index].Start); end = Math.Max(end, ranges[index].End);
                    ranges.RemoveAt(index);
                }
                ranges.Insert(index, new(start, end));
            }
        }
        ct.ThrowIfCancellationRequested();
        PrivateFiles.RejectLinks(destination); File.Move(partial, destination, false);
        await secrets.DeleteAsync(key, ct);
        return null;
    }
}
