using System.Security.Cryptography;
using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class DouyinCovers(string probe, string privateTemp, HttpClient? client = null, TimeSpan? budget = null)
{
    public async Task<MediaFile> DownloadAsync(VideoJob job, JsonArray? thumbnails, Func<Task> checkpoint, CancellationToken ct)
    {
        var groups = DownloadPolicy.Covers(thumbnails).ToArray();
        ct.ThrowIfCancellationRequested();
        var work = PrivateFiles.NewTemporaryDirectory(privateTemp);
        try
        {
        using var ownedClient = client is null ? DouyinHttp.Create() : null;
        var http = client ?? ownedClient!;
        using var coverBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        coverBudget.CancelAfter(budget ?? TimeSpan.FromSeconds(30));
        var folder = job.DownloadDirectory!; var stem = Rules.FileStem(job.Name);
        PrivateFiles.RejectLinks(folder, recursive: true);
        var originalMedia = job.Media!;
        var saved = new List<MediaCover>();
        foreach (var old in originalMedia.Covers ?? [])
            if (File.Exists(old.Path) && Path.GetFullPath(Path.GetDirectoryName(old.Path)!) == Path.GetFullPath(folder)
                && await HashAsync(old.Path, ct) == old.Sha256) saved.Add(old);
        var refreshed = new HashSet<string>();
        foreach (var group in groups)
        {
            var temp = Path.Combine(work, group.Key + ".cover.tmp");
            try
            {
                MediaCover? image = null; string? extension = null;
                foreach (var candidate in group)
                {
                    if (coverBudget.IsCancellationRequested) break;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(coverBudget.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
                    try
                    {
                        using var response = await DouyinHttp.GetAsync(http, candidate["url"]!.ToString(), job.Source!.CanonicalUrl, timeout.Token);
                        if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri?.Scheme != "https" || response.Content.Headers.ContentLength > 16_000_000) continue;
                        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
                        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0) { if (bytes.Length + read > 16_000_000) throw new IOException("Cover too large"); bytes.Write(buffer, 0, read); }
                        var data = bytes.ToArray();
                        if (!IsStaticImage(data)) continue;
                        await File.WriteAllBytesAsync(temp, data, timeout.Token);
                        var result = await DouyinSource.RunAsync(ToolIntegrity.Verify(probe), ["-v", "quiet", "-protocol_whitelist", "file,pipe", "-show_entries", "stream=width,height,codec_name", "-of", "json", temp], null, timeout.Token);
                        if (result.ExitCode != 0) continue;
                        var stream = JsonNode.Parse(result.Output)?["streams"]?[0];
                        extension = stream?["codec_name"]?.ToString() switch { "mjpeg" => ".jpeg", "png" => ".png", "webp" => ".webp", _ => null };
                        var width = (int)DownloadPolicy.Number(stream?["width"]); var height = (int)DownloadPolicy.Number(stream?["height"]);
                        if (extension is null || width <= 0 || height <= 0) continue;
                        if (group.Key == "horizontal_cover" && width <= height) continue;
                        image = new(temp, width, height, candidate["source_field"]?.ToString() ?? group.Key, Convert.ToHexString(SHA256.HashData(data))); break;
                    }
                    catch (HttpRequestException) { }
                    catch (NeedsActionException) { }
                    catch (IOException) { }
                    catch (System.Text.Json.JsonException) { }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                }
                if (image is null) continue;
                // Use actual image geometry: Douyin's reported width/height can be wrong.
                // Preserve source pixels; do not invent a missing orientation by cropping.
                if (refreshed.Contains(image.Orientation)) continue;
                var target = Path.Combine(folder, stem + " - bìa " + image.Orientation + extension);
                if (File.Exists(target))
                {
                    var hash = await HashAsync(target, ct);
                    if (hash != image.Sha256)
                    {
                        if (originalMedia.Covers?.Any(x => x.Path == target && x.Sha256 == hash) != true)
                            throw new NeedsActionException("Tên ảnh bìa đích đang chứa file khác. Giữ file đó ở tên khác trước khi thử lại để tránh ghi đè.");
                        Backup(target, job.Id);
                    }
                }
                if (!File.Exists(target)) File.Move(temp, target);
                var oldCover = originalMedia.CoverPath;
                if (oldCover is not null && File.Exists(oldCover) && Path.GetFullPath(Path.GetDirectoryName(oldCover)!) == Path.GetFullPath(folder)
                    && Path.GetFileNameWithoutExtension(oldCover) == stem + " - ảnh bìa" && oldCover != target) Backup(oldCover, job.Id);
                foreach (var old in saved.Where(x => x.Orientation == image.Orientation && x.Path != target).ToArray())
                    if (File.Exists(old.Path) && await HashAsync(old.Path, ct) == old.Sha256) Backup(old.Path, job.Id);
                saved.RemoveAll(x => x.Orientation == image.Orientation);
                saved.Add(image with { Path = target }); refreshed.Add(image.Orientation);
                job.Media = originalMedia with { Covers = saved.ToArray(), CoverPath = saved[0].Path, CoverStatus = null };
                await checkpoint();
            }
            catch (NeedsActionException) { /* A conflicting cover must not block a valid video. */ }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        ct.ThrowIfCancellationRequested();
        var missing = new[] { "dọc", "ngang" }.Where(direction => !saved.Any(x => x.Orientation == direction)).ToArray();
        return job.Media! with { Covers = saved, CoverPath = saved.FirstOrDefault()?.Path,
            CoverStatus = $"Đã tải {saved.Count} bìa nguồn ({string.Join(", ", saved.Select(x => x.Orientation))}); giữ tỷ lệ nguồn"
                + (missing.Length == 0 ? "" : $". Chưa lấy được bìa {string.Join(", ", missing)}; không thay bằng khung hình video") };
        }
        finally
        {
            try { PrivateFiles.RejectLinks(work, recursive: true); Directory.Delete(work, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (NeedsActionException) { }
        }
    }
    private void Backup(string path, string jobId)
    {
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(privateTemp))!, "cover-backups", jobId);
        PrivateFiles.EnsureDirectory(folder);
        PrivateFiles.RejectLinks(path);
        var target = Path.Combine(folder, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
        File.Move(path, target);
    }
    public static bool IsStaticImage(byte[] data)
    {
        if (data.Length < 12) return false;
        if (data[0] == 0xff && data[1] == 0xd8) return true;
        if (data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            for (var pos = 8; pos + 12 <= data.Length;)
            {
                var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
                if (data.AsSpan(pos + 4, 4).SequenceEqual("acTL"u8)) return false;
                if (size > data.Length - pos - 12) return false;
                pos += (int)size + 12;
            }
            return true;
        }
        if (!data.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !data.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return false;
        for (var pos = 12; pos + 8 <= data.Length;)
        {
            var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4, 4));
            var kind = data.AsSpan(pos, 4);
            if (kind.SequenceEqual("ANIM"u8) || kind.SequenceEqual("ANMF"u8)) return false;
            if (size > data.Length - pos - 8) return false;
            if (kind.SequenceEqual("VP8X"u8) && size > 0 && (data[pos + 8] & 2) != 0) return false;
            pos += 8 + (int)size + ((int)size & 1);
        }
        return true;
    }
    private static async Task<string> HashAsync(string path, CancellationToken ct) { await using var input = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(input, ct)); }
}
