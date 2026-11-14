using System.Text.Json;
using System.Text.Json.Nodes;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

// Signed CDN URLs are sensitive; keep the job-bound plan in the private DPAPI store.
public sealed class DouyinDownloadMetadata(ISecretStore secrets)
{
    private sealed record Saved(string SourceId, string? AttemptId, string? Directory, JsonNode Info);
    private static string Key(VideoJob job) => "source-" + job.Id;
    public async Task<JsonNode?> ReadAsync(VideoJob job, CancellationToken ct)
    {
        try
        {
            var raw = await secrets.ReadAsync(Key(job), ct);
            if (raw is null || raw.Length > 4 * 1024 * 1024) return null;
            var saved = JsonSerializer.Deserialize<Saved>(raw);
            if (saved is null || saved.SourceId != job.Source?.Id || !Valid(saved.Info, job.Source!)) return null;
            var sameAttempt = saved.AttemptId == job.DownloadAttemptId && saved.Directory == job.DownloadDirectory;
            // A new transfer has its own byte receipt, but can reuse media URLs from a
            // previous completed download of this exact job/source. Never reuse an offset.
            var previousDownload = job.Receipts.Any(r => r.Action == JobActions.Download && r.AttemptId == saved.AttemptId
                && r.Directory == saved.Directory && r.Media is not null);
            return sameAttempt || previousDownload ? saved.Info : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or System.Security.Cryptography.CryptographicException) { return null; }
    }
    public Task WriteAsync(VideoJob job, JsonNode info, CancellationToken ct)
    {
        if (job.Source is null || !Valid(info, job.Source)) throw new NeedsActionException("Metadata tải không khớp công việc. App chưa lưu link tải.");
        // Retain only media/poster metadata, never page state or HTTP cookie headers.
        var clean = new JsonObject();
        foreach (var name in new[] { "id", "source_duration_ms", "source_sec_uid", "metadata_source" })
            clean[name] = info[name]?.DeepClone();
        clean["formats"] = CopyObjects(info["formats"], ["format_id", "url", "ext", "vcodec", "acodec", "preference", "width", "height", "fps", "tbr", "filesize", "filesize_approx", "format_note"]);
        clean["thumbnails"] = CopyObjects(info["thumbnails"], ["id", "url", "width", "height", "source_field"]);
        var raw = JsonSerializer.Serialize(new Saved(job.Source.Id, job.DownloadAttemptId, job.DownloadDirectory, clean));
        if (raw.Length > 4 * 1024 * 1024) throw new NeedsActionException("Metadata tải vượt giới hạn an toàn.");
        return secrets.WriteAsync(Key(job), raw, ct);
    }
    public Task DeleteAsync(VideoJob job, CancellationToken ct) => secrets.DeleteAsync(Key(job), ct);
    private static JsonArray CopyObjects(JsonNode? value, string[] names)
    {
        var output = new JsonArray();
        if (value is not JsonArray items) return output;
        foreach (var item in items.OfType<JsonObject>())
        {
            var copy = new JsonObject();
            foreach (var name in names) if (item[name] is JsonValue field) copy[name] = field.DeepClone();
            output.Add(copy);
        }
        return output;
    }
    private static bool Valid(JsonNode info, SourceVideo source) => info is JsonObject && info["id"]?.ToString() == source.Id
        && info["formats"] is JsonArray formats && formats.Count <= 512 && formats.All(f => f is JsonObject)
        && formats.Any(f => f is JsonObject && DownloadPolicy.Usable(f) && DouyinHttp.Allowed(f["url"]?.ToString() ?? ""));
}
