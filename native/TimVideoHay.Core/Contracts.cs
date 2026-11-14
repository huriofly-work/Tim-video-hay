namespace TimVideoHay.Core;

public sealed record DouyinChannelSnapshot(string SecUid, string Name, long Followers, IReadOnlyList<ChannelVideo> Videos);
public interface IDouyinBrowserReader
{
    Task<DouyinChannelSnapshot> ReadAsync(SourceVideo source, string? secUid, CancellationToken ct);
}

public interface ISourceChannelReader
{
    Task<SourceChannelMetadata> ReadAsync(VideoJob job, CancellationToken ct);
}

public interface IJobStore
{
    Task SaveAsync(VideoJob job, CancellationToken ct = default);
    Task<IReadOnlyList<VideoJob>> ListAsync(CancellationToken ct = default);
    Task<AppSettings> SettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default);
}
public interface ISecretStore
{
    Task<string?> ReadAsync(string key, CancellationToken ct = default);
    Task WriteAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
public interface IVideoSource
{
    Task<string?> FindMovedFolderAsync(VideoJob job, IReadOnlyList<string> roots, Func<Task> checkpoint, CancellationToken ct) => Task.FromResult<string?>(null);
    Task RelocateFolderAsync(VideoJob job, string directory, Func<Task> checkpoint, CancellationToken ct) => throw new NeedsActionException("Nguồn này chưa hỗ trợ liên kết thư mục đã chuyển.");
    Task CheckDuplicateAsync(VideoJob job, CancellationToken ct) => Task.CompletedTask;
    Task MigrateOutputStateAsync(VideoJob job) => Task.CompletedTask;
    Task RestoreDownloadProgressAsync(VideoJob job) => Task.CompletedTask;
    Task<SourceVideo> ResolveAsync(string input, CancellationToken ct);
    Task<SourceMetadata> ReadMetadataAsync(SourceVideo source, CancellationToken ct);
    Task<MediaFile> DownloadAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct);
    Task<MediaFile> RefreshCoversAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
        => DownloadAsync(job, checkpoint, new DirectProgress<double>(_ => { }), ct);
    Task ValidateFileAsync(MediaFile file, CancellationToken ct);
    Task ValidateFolderDeletionAsync(VideoJob job, CancellationToken ct);
    Task DeleteFolderAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct);
}
public interface ISheetDestination
{
    Task<int?> FindExistingRowAsync(VideoJob job, CancellationToken ct) => Task.FromResult<int?>(job.SheetRow ?? 0);
    Task CheckDuplicateAsync(VideoJob job, CancellationToken ct) => Task.CompletedTask;
    Task PrepareAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) => Task.CompletedTask;
    Task PreflightAsync(SheetTarget target, CancellationToken ct);
    Task WriteAndVerifyAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct);
    Task DeleteSheetAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct);
}
public interface IYouTubeDestination
{
    Task<bool> InspectPendingUploadAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) => Task.FromResult(false);
    Task<bool> ExistsAsync(VideoJob job, CancellationToken ct) => Task.FromResult(true);
    Task CheckDuplicateAsync(VideoJob job, CancellationToken ct) => Task.CompletedTask;
    Task PreflightAsync(ChannelTarget target, CancellationToken ct);
    Task UploadAndVerifyAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct);
    Task DeleteVideoAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct);
}

