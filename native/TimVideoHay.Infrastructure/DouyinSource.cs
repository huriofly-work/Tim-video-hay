using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class DouyinSource(string toolsDirectory, string privateTemp, ISecretStore secrets, IDouyinVideoBrowserReader browser, Func<HttpClient>? mediaClient = null, Action<string>? notify = null) : IVideoSource
{
    private static readonly string[] MediaExtensions = [".mp4", ".webm", ".mov", ".mkv"];
    private string OutputState => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(privateTemp))!, "download-state");
    public async Task RestoreDownloadProgressAsync(VideoJob job)
    {
        if (job.HasDownload && File.Exists(job.Media!.Path))
        { job.ProgressAction = JobActions.Download; job.DownloadedBytes = job.Media.Length; job.DownloadTotalBytes = job.Media.Length; job.Progress = 100; return; }
        if (job.FolderDeleted || job.Media is not null || job.DownloadDirectory is null || !Directory.Exists(job.DownloadDirectory)
            || !MediaExtensions.Contains("." + job.DownloadExtension) || job.DownloadFormatId is null) return;
        _ = OwnedDownloadDirectory.Validate(job, OutputState);
        await MediaDownload.RestoreProgressAsync(job, Path.Combine(job.DownloadDirectory, Rules.FileStem(job.Name) + "." + job.DownloadExtension), secrets);
    }
    public Task<string?> FindMovedFolderAsync(VideoJob job, IReadOnlyList<string> roots, Func<Task> checkpoint, CancellationToken ct)
        => new MovedFolderFinder(OutputState, secrets).FindAsync(job, roots, checkpoint, ct);
    public Task RelocateFolderAsync(VideoJob job, string directory, Func<Task> checkpoint, CancellationToken ct)
        => MovedDownloadDirectory.RelinkAsync(job, directory, OutputState, secrets, checkpoint, ct);
    public Task CheckDuplicateAsync(VideoJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); DownloadPolicy.CheckDirectory(job, OutputState); return Task.CompletedTask;
    }
    public async Task<SourceMetadata> ReadMetadataAsync(SourceVideo source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = await browser.ReadVideoMetadataAsync(source, ct, readCovers: false);
        if (info["id"]?.ToString() == source.Id && info["source_duration_ms"] is JsonValue value && value.TryGetValue<long>(out var ms) && ms > 0)
            return new(source.Id, ms, info["metadata_source"]?.ToString() ?? "Douyin", DateTimeOffset.UtcNow);
        throw new NeedsActionException("Không lấy được thời lượng Douyin của đúng video. Bấm Tiếp tục để thử lại; app chưa ghi bảng.");
    }
    public Task ValidateFolderDeletionAsync(VideoJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); _ = OwnedDownloadDirectory.Validate(job, OutputState); return Task.CompletedTask;
    }
    public async Task DeleteFolderAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    { await OwnedDownloadDirectory.DeleteAsync(job, OutputState, checkpoint, ct); await new DouyinDownloadMetadata(secrets).DeleteAsync(job, ct); }
    public Task MigrateOutputStateAsync(VideoJob job)
    {
        if (job.DownloadDirectory is not null && job.Source is not null && File.Exists(Path.Combine(job.DownloadDirectory, ".timvideohay-owner")))
            _ = DownloadPolicy.ReserveDirectory(job, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(privateTemp))!, "download-state"));
        if (job.DownloadDirectory is not null && job.Source is not null && Directory.Exists(job.DownloadDirectory) && !job.FolderDeleted)
        {
            DownloadPolicy.CheckDirectory(job, OutputState);
            job.DirectoryIdentity ??= WindowsFileIdentity.Capture(job.DownloadDirectory);
            if (job.Media is not null && File.Exists(job.Media.Path)) job.VideoIdentity ??= WindowsFileIdentity.Capture(job.Media.Path);
        }
        return Task.CompletedTask;
    }
    public async Task<SourceVideo> ResolveAsync(string input, CancellationToken ct)
    {
        var uri = Rules.DouyinUri(input);
        using var handler = DouyinHttp.Handler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        for (var i = 0; i < 6; i++)
        {
            uri = Rules.DouyinUri(uri.AbsoluteUri);
            var id = Rules.SourceId(uri);
            if (id is not null) return new(id, "https://www.douyin.com/video/" + id);
            if (uri.Host != "v.douyin.com") break;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.Headers.Location is null) break;
            uri = new Uri(uri, response.Headers.Location);
        }
        throw new NeedsActionException("Không nhận diện được video. Dùng link Douyin đầy đủ /video/... thay link chia sẻ.");
    }
    public async Task<MediaFile> DownloadAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct)
    {
        if (job.Source is null) throw new InvalidOperationException("Chưa nhận diện nguồn.");
        var folder = DownloadPolicy.ReserveDirectory(job, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(privateTemp))!, "download-state"));
        await checkpoint();
        var stem = Rules.FileStem(job.Name);
        PrivateFiles.EnsureDirectory(privateTemp);
        try
        {
            var plans = new DouyinDownloadMetadata(secrets);
            async Task<JsonNode> FreshAsync(bool invalidate = false)
            {
                if (invalidate) browser.Forget(job.Source);
                var fresh = await browser.ReadVideoMetadataAsync(job.Source, ct);
                await plans.WriteAsync(job, fresh, ct); return fresh;
            }
            var info = await plans.ReadAsync(job, ct);
            if (info is null) info = job.Media is null ? await FreshAsync() : new JsonObject { ["id"] = job.Source.Id, ["formats"] = new JsonArray() };
            var formats = Formats(info);
            if (job.Media is null && job.DownloadFormatId is not null && !formats.Any(f => f["format_id"]?.ToString() == job.DownloadFormatId))
            { info = await FreshAsync(invalidate: true); formats = Formats(info); }
            if (job.Media is null && formats.Length == 0) throw new NeedsActionException("Douyin chưa cung cấp bản video có hình và âm thanh để tải. Kiểm tra nguồn rồi Tiếp tục.");
            if (job.Media is null && job.DownloadFormatId is null)
            {
                // Douyin download_addr dimensions can describe a preview, not the actual MP4.
                // Probe every distinct rendition; CDN mirrors share the same rendition.
                var candidates = new List<VideoVariant>();
                foreach (var group in formats.GroupBy(x => DownloadPolicy.MirrorGroup(x["format_id"]!.ToString())))
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var format in group)
                    {
                        var actual = await ProbeRemoteAsync(format, ct);
                        if (actual is null) continue;
                        if (actual.HasAudio) { candidates.Add(actual); break; }
                        // Some addresses under one rate are video-only while another
                        // mirror is muxed. Validate the exact URL selected for download.
                    }
                }
                job.DownloadFormatId = DownloadPolicy.Best(candidates).Id;
                job.DownloadExtension = formats.First(x => x["format_id"]!.ToString() == job.DownloadFormatId)["ext"]!.ToString();
                await checkpoint();
            }
            if (job.Media is null)
            {
                if (!formats.Any(x => x["format_id"]!.ToString() == job.DownloadFormatId))
                    throw new NeedsActionException("Bản chất lượng đang tải đã thay đổi trên Douyin. Dùng công việc mới với tên khác để tránh nối nhầm file dở dang.");
                var selected = formats.First(x => x["format_id"]!.ToString() == job.DownloadFormatId);
                // Preserve the container extension as well as the format across restarts.
                // Older checkpoints can recover it from a nonempty owned partial file.
                if (job.DownloadExtension is null)
                {
                    var partials = MediaExtensions.Where(ext => new[] { Path.Combine(folder, stem + ext), Path.Combine(folder, stem + ext + ".part") }.Any(path => File.Exists(path) && new FileInfo(path).Length > 0)).ToArray();
                    if (partials.Length > 1) throw new NeedsActionException("Có nhiều file tải dở khác loại trong công việc này. Cần kiểm tra trước khi tiếp tục để không nối nhầm.");
                    if (partials.Length == 1) job.DownloadExtension = partials[0].TrimStart('.');
                    else { _ = await ProbeRemoteAsync(selected, ct); job.DownloadExtension = selected["ext"]!.ToString(); }
                    await checkpoint();
                }
                if (!MediaExtensions.Contains("." + job.DownloadExtension)) throw new NeedsActionException("Loại file nguồn lưu trong checkpoint không hợp lệ.");
                selected["ext"] = job.DownloadExtension;
                _ = OwnedDownloadDirectory.Validate(job, OutputState);
                await plans.WriteAsync(job, info, ct);
                using var http = mediaClient?.Invoke() ?? DouyinHttp.Create();
                var destination = Path.Combine(folder, stem + "." + job.DownloadExtension);
                try { await new MediaDownload(http, secrets).DownloadAsync(job, selected["url"]!.ToString(), destination, progress, ct, checkpoint); }
                catch (DouyinMediaUrlExpiredException)
                {
                    notify?.Invoke("Link tải cũ hết hạn/bị từ chối. App tự lấy link mới của cùng bản video, giữ phần tải dở để kiểm tra và tải tiếp.");
                    info = await FreshAsync(invalidate: true); formats = Formats(info);
                    selected = formats.FirstOrDefault(f => f["format_id"]?.ToString() == job.DownloadFormatId)
                        ?? throw new NeedsActionException("Nguồn không còn bản chất lượng đang tải. Đã giữ file dở để tránh nối nhầm video.");
                    selected["ext"] = job.DownloadExtension;
                    // One refresh only. The download receipt still checks source/format/validator/length.
                    await new MediaDownload(http, secrets).DownloadAsync(job, selected["url"]!.ToString(), destination, progress, ct, checkpoint);
                }
                var files = Directory.EnumerateFiles(folder).Where(x => Path.GetFileNameWithoutExtension(x) == stem && MediaExtensions.Contains(Path.GetExtension(x).ToLowerInvariant())).ToArray();
                if (files.Length != 1) throw new NeedsActionException("Không tìm thấy đúng một file video hoàn chỉnh sau khi tải.");
                job.Media = await ProbeAsync(files[0], ct) with { FormatId = job.DownloadFormatId,
                    QualityStatus = job.DownloadFormatId!.StartsWith("download_addr-original", StringComparison.Ordinal) || job.DownloadFormatId.StartsWith("download_addr-improved", StringComparison.Ordinal)
                        ? "Đã lấy bản nguồn ratio=default; không chuyển mã"
                        : "Đã đối chiếu các bản chất lượng web; giữ nguyên luồng nguồn" };
                var previous = job.Receipts.LastOrDefault(x => x.Action == JobActions.Download && !x.Deleted && x.Directory == job.DownloadDirectory)?.Media;
                if (previous is not null) job.Media = job.Media with { Covers = previous.Covers, CoverPath = previous.CoverPath }; // Covers still pass their own path/hash validation below.
                await checkpoint(); // A failed cover download retries only the cover, not the video.
            }
            await ValidateFileAsync(job.Media, ct);
            var updated = await DownloadCoversAsync(job, info, checkpoint, ct);
            job.VideoIdentity = WindowsFileIdentity.Capture(updated.Path);
            progress.Report(100); return updated;
        }
        catch (Exception ex) when (job.Media is not null && !ct.IsCancellationRequested &&
            ex is NeedsActionException or HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
        {
            // A previously downloaded video may proceed even if cover metadata expires.
            // Revalidate its bytes so this fallback never bypasses a damaged video.
            await ValidateFileAsync(job.Media, ct);
            progress.Report(100);
            job.VideoIdentity = WindowsFileIdentity.Capture(job.Media.Path);
            return job.Media with { CoverStatus = "Chưa lấy đủ ảnh bìa nguồn; đã bỏ qua ảnh bìa để tiếp tục video." };
        }
    }
    public async Task<MediaFile> RefreshCoversAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        if (job.Source is null || job.Media is null) throw new NeedsActionException("Chưa có video hoàn chỉnh để tải lại ảnh bìa.");
        ct.ThrowIfCancellationRequested();
        _ = OwnedDownloadDirectory.Validate(job, OutputState);
        await ValidateFileAsync(job.Media, ct);
        try
        {
            // An explicit cover retry must not reuse an empty/expired poster list.
            // This path never selects or transfers a video rendition.
            browser.Forget(job.Source);
            var info = await browser.ReadVideoMetadataAsync(job.Source, ct);
            await new DouyinDownloadMetadata(secrets).WriteAsync(job, info, ct);
            return await DownloadCoversAsync(job, info, checkpoint, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is NeedsActionException or HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
        {
            return job.Media with { CoverStatus = "Chưa lấy đủ ảnh bìa nguồn; thử tải lại ảnh bìa sau." };
        }
    }
    private async Task<MediaFile> DownloadCoversAsync(VideoJob job, JsonNode info, Func<Task> checkpoint, CancellationToken ct)
    {
        using var http = mediaClient?.Invoke();
        return await new DouyinCovers(Tool("ffprobe.exe"), privateTemp, http).DownloadAsync(job, info["thumbnails"] as JsonArray, checkpoint, ct);
    }
    private static JsonNode[] Formats(JsonNode info) => info["formats"] is JsonArray formats ? formats.Where(x => x is not null && DownloadPolicy.Usable(x)
        && DouyinHttp.Allowed(x["url"]?.ToString() ?? "") && Regex.IsMatch(x["format_id"]?.ToString() ?? "", @"^[A-Za-z0-9_.-]+$")).Select(x => x!).ToArray() : [];
    private async Task<VideoVariant?> ProbeRemoteAsync(JsonNode format, CancellationToken ct)
    {
        var uri = DouyinHttp.Validate(format["url"]?.ToString() ?? "");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var bridge = new MediaRangeBridge(uri.AbsoluteUri, "https://www.douyin.com/", timeout.Token);
            var result = await RunAsync(Tool("ffprobe.exe"), ["-v", "quiet", "-protocol_whitelist", "http,tcp", "-rw_timeout", "5000000", "-show_entries", "stream=codec_type,codec_name,width,height,r_frame_rate,bit_rate:format=bit_rate,size:format_tags=major_brand", "-of", "json", bridge.Url], null, timeout.Token);
            if (result.ExitCode != 0) return null;
            var json = JsonNode.Parse(result.Output)!;
            var video = json["streams"]?.AsArray().FirstOrDefault(x => x?["codec_type"]?.ToString() == "video");
            if (video is null) return null;
            if (json["format"]?["tags"]?["major_brand"]?.ToString().Trim() == "qt") format["ext"] = "mov";
            var fpsParts = video["r_frame_rate"]?.ToString().Split('/') ?? [];
            var fps = fpsParts.Length == 2 && DownloadPolicy.Number(JsonValue.Create(fpsParts[1])) > 0
                ? DownloadPolicy.Number(JsonValue.Create(fpsParts[0])) / DownloadPolicy.Number(JsonValue.Create(fpsParts[1])) : 0;
            return DownloadPolicy.Variant(format) with { Width = (int)DownloadPolicy.Number(video["width"]), Height = (int)DownloadPolicy.Number(video["height"]), Fps = fps,
                Bitrate = DownloadPolicy.Number(video["bit_rate"] ?? json["format"]?["bit_rate"]) / 1000, Codec = video["codec_name"]?.ToString() ?? "", Probed = true,
                Size = (long)DownloadPolicy.Number(json["format"]?["size"]),
                HasAudio = json["streams"]!.AsArray().Any(x => x?["codec_type"]?.ToString() == "audio") };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
    }
    private string Tool(string name)
    {
        var path = Path.Combine(toolsDirectory, name);
        return ToolIntegrity.Verify(path);
    }
    public async Task<MediaFile> ProbeAsync(string path, CancellationToken ct)
    {
        PrivateFiles.RejectLinks(path);
        var result = await RunAsync(Tool("ffprobe.exe"), ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height", "-of", "json", Path.GetFullPath(path)], null, ct);
        if (result.ExitCode != 0) throw new NeedsActionException("File tải về không phải video đọc được. Chưa ghi bảng hoặc upload.");
        var json = JsonNode.Parse(result.Output)!;
        var streams = json["streams"]!.AsArray().Select(x => x!["codec_type"]!.GetValue<string>()).ToArray();
        if (!streams.Contains("video") || !streams.Contains("audio")) throw new NeedsActionException("Nguồn không cung cấp file có cả hình và âm thanh. App không ghép/chuyển mã.");
        var video = json["streams"]!.AsArray().First(x => x!["codec_type"]!.ToString() == "video")!;
        var seconds = double.Parse(json["format"]!["duration"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        _ = Rules.VideoType(seconds);
        await using var input = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        var mime = Path.GetExtension(path).ToLowerInvariant() switch { ".webm" => "video/webm", ".mkv" => "video/x-matroska", ".mov" => "video/quicktime", _ => "video/mp4" };
        return new(Path.GetFullPath(path), seconds, input.Length, hash, mime, (int)DownloadPolicy.Number(video["width"]), (int)DownloadPolicy.Number(video["height"]), video["codec_name"]?.ToString());
    }
    public async Task ValidateFileAsync(MediaFile file, CancellationToken ct)
    {
        PrivateFiles.RejectLinks(file.Path);
        if (!File.Exists(file.Path)) throw new NeedsActionException("File đã bị xóa hoặc di chuyển. Khôi phục file về đường dẫn đã lưu trước khi tiếp tục.");
        await using var input = File.OpenRead(file.Path);
        if (input.Length != file.Length || Convert.ToHexString(await SHA256.HashDataAsync(input, ct)) != file.Sha256)
            throw new NeedsActionException("File đã thay đổi sau khi tải. Đã dừng để không đăng nhầm nội dung.");
    }
    internal sealed record ProcessResult(int ExitCode, string Output, string Error);
    internal static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> args, Action<string>? line, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var cancel = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        var output = new System.Text.StringBuilder();
        var stdout = Task.Run(async () => { while (await process.StandardOutput.ReadLineAsync(ct) is string value) { if (output.Length < 16_000_000) output.AppendLine(value); line?.Invoke(value); } }, ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct); await stdout;
        return new(process.ExitCode, output.ToString(), await stderr);
    }
}
