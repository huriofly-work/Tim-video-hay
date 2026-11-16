namespace TimVideoHay.Core;

public sealed partial class JobCoordinator
{
    private sealed class JobActivity(VideoJob job, CancellationTokenSource cancellation, string kind)
    {
        public VideoJob Job { get; } = job;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public string Kind { get; } = kind;
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly Dictionary<string, JobActivity> activities = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> resourceGates = new(StringComparer.Ordinal);
    private bool stopping;
    private readonly SemaphoreSlim sourceGate = new(1, 1);
    private readonly SemaphoreSlim backgroundGate = new(4, 4);
    private int sourceUsers;
    public bool IsExecuting { get { lock (sync) return activities.Count > 0; } }
    public bool IsJobBusy(string id) { lock (sync) return activities.ContainsKey(id); }
    public bool IsUsingSource { get { lock (sync) return sourceUsers > 0; } }
    public bool UsesCredential(string credential)
    {
        lock (sync) return activities.Values.Any(x =>
        {
            var actions = x.Kind is "covers" or "folder" ? JobActions.None
                : x.Kind == "inspect" ? ((x.Job.SheetVerified || x.Job.UnknownSheet) && !x.Job.SheetDeleted ? JobActions.WriteSheet : 0) | ((x.Job.YouTubeId is not null || x.Job.UnknownUpload) && !x.Job.YouTubeDeleted ? JobActions.UploadYouTube : 0)
                : x.Job.DeletionRequested ? (x.Job.DeleteSheetRequested && !x.Job.SheetDeleted ? JobActions.WriteSheet : 0) | (x.Job.DeleteYouTubeRequested && !x.Job.YouTubeDeleted ? JobActions.UploadYouTube : 0)
                : x.Job.Work;
            return x.Kind is ("work" or "sheet") && x.Job.BlockListSheet?.CredentialId == credential
                || actions.HasFlag(JobActions.WriteSheet) && x.Job.Sheet?.CredentialId == credential
                || actions.HasFlag(JobActions.UploadYouTube) && x.Job.Channel?.CredentialId == credential;
        });
    }
    public Task<T> OpenSourceSessionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => WithSourceAsync(() => action(ct), ct);
    private async Task<T> WithSourceAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await sourceGate.WaitAsync(ct);
        try { lock (sync) sourceUsers++; return await action(); }
        finally { lock (sync) sourceUsers--; sourceGate.Release(); }
    }
    private Task WithSourceAsync(Func<Task> action, CancellationToken ct)
        => WithSourceAsync(async () => { await action(); return true; }, ct);

    // Called under gate: reserve a job before releasing the short scheduler lock.
    private JobActivity Reserve(VideoJob job, CancellationToken ct, string kind)
    {
        lock (sync)
        {
            if (stopping) throw new NeedsActionException("Đang dừng ứng dụng; chờ lưu checkpoint rồi thử lại.");
            if (activities.ContainsKey(job.Id)) throw new NeedsActionException("Công việc này đang có thao tác khác. Chờ thao tác đó hoàn tất.");
            var activity = new JobActivity(job, CancellationTokenSource.CreateLinkedTokenSource(ct), kind);
            activities.Add(job.Id, activity); return activity;
        }
    }
    private void Release(JobActivity activity)
    {
        lock (sync)
        {
            activities.Remove(activity.Job.Id);
            activity.Cancellation.Dispose();
            activity.Finished.TrySetResult();
            if (wake.CurrentCount == 0) wake.Release();
        }
        if (activity.Kind is "work" or "sheet" or "delete" or "verify") Start();
    }
    private static bool IsMediaWork(VideoJob job) => !job.DeletionRequested && !CanVerifyExisting(job)
        && (job.Work & (JobActions.Download | JobActions.UploadYouTube)) != 0;
    private bool CanDispatch(VideoJob job)
    {
        lock (sync) return IsMediaWork(job) ? !activities.Values.Any(x => x.Kind == "work")
            : activities.Values.Count(x => x.Kind is "sheet" or "verify" or "delete") < 4;
    }
    private async Task<T> ExecuteActivityAsync<T>(JobActivity activity, Func<CancellationToken, Task<T>> action)
    {
        try
        {
            var token = activity.Cancellation.Token;
            if (activity.Kind is "work" or "covers" or "folder") return await WithFolderAsync(activity.Job, () => action(token), token);
            if (activity.Kind is "sheet" or "verify")
            {
                await backgroundGate.WaitAsync(token);
                try { return await action(token); }
                finally { backgroundGate.Release(); }
            }
            return await action(token);
        }
        catch (Exception ex) when (activity.Kind is "work" or "sheet" or "delete" or "verify")
        {
            if (ex is OperationCanceledException && activity.Cancellation.IsCancellationRequested)
            {
                if (preserveYouTubeChecks && CanVerifyExisting(activity.Job)) await WaitForYouTube(activity.Job, "Đã lưu; tự kiểm tra YouTube khi mở app lại.");
                else { activity.Job.State = JobState.Paused; activity.Job.Message = "Đã lưu checkpoint. Bấm Tiếp tục để chạy lại."; }
            }
            else SetIssue(activity.Job, ex, SafeError(ex));
            try { await Save(activity.Job); }
            catch { Changed?.Invoke(activity.Job); } // Retain the failure in the live desktop if storage is unavailable.
            return default!;
        }
        finally { Release(activity); }
    }
    private Task ExecuteActivityAsync(JobActivity activity, Func<CancellationToken, Task> action)
        => ExecuteActivityAsync(activity, async ct => { await action(ct); return true; });

    private async Task WithSheetAsync(VideoJob job, Func<Task> action, CancellationToken ct)
    {
        if (job.Sheet is null) { await action(); return; }
        // Both video and source-channel tabs can be updated by one atomic write.
        var resource = resourceGates.GetOrAdd("sheet:" + job.Sheet.SpreadsheetId, _ => new(1, 1));
        await resource.WaitAsync(ct);
        try { await action(); }
        finally { resource.Release(); }
    }
    private async Task<T> WithFolderAsync<T>(VideoJob job, Func<Task<T>> action, CancellationToken ct)
    {
        var path = job.DownloadDirectory ?? Path.Combine(job.Folder, Rules.FileStem(job.Name));
        var resource = resourceGates.GetOrAdd("folder:" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant(), _ => new(1, 1));
        await resource.WaitAsync(ct);
        try { return await action(); }
        finally { resource.Release(); }
    }
    private Task WithFolderAsync(VideoJob job, Func<Task> action, CancellationToken ct)
        => WithFolderAsync(job, async () => { await action(); return true; }, ct);

    private void Launch(JobActivity activity) => _ = Task.Run(() => ExecuteActivityAsync(activity, ct => RunAsync(activity.Job, ct)));

    private async Task DeleteResultsAsync(VideoJob job, CancellationToken ct)
    {
        // Validate the selected folder before any mutation, retaining the ownership guard.
        if (job.DeleteFolderRequested && !job.FolderDeleted) await WithFolderAsync(job, () => source.ValidateFolderDeletionAsync(job, ct), ct);
        var failures = new List<(Exception Error, JobActions Action, string Message)>();
        async Task Part(JobActions action, string stage, Func<Task> execute)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await Stage(job, stage, action);
                if (action != JobActions.Download)
                {
                    var jobs = await store.ListAsync(ct);
                    // Check only the selected remote part; pending downloads never delay local deletion.
                    var part = new VideoJob { DeletionRequested = true, Sheet = job.Sheet, Channel = job.Channel,
                        DeleteSheetRequested = action == JobActions.WriteSheet, DeleteYouTubeRequested = action == JobActions.UploadYouTube,
                        Actions = JobActions.None, PendingActions = JobActions.None };
                    if (BlockingAuthentication(part, jobs) is { } auth) throw new NeedsAuthException(auth.Message);
                    if (BlockingRetry(part, jobs, DateTimeOffset.UtcNow) is { } retry)
                        throw new RetryLaterException(retry.Message, retry.RetryAt!.Value, retry.DependencyKey!);
                }
                if (action == JobActions.Download) await execute();
                else
                {
                    await backgroundGate.WaitAsync(ct);
                    try { await execute(); }
                    finally { backgroundGate.Release(); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures.Add((ex, action, ex is NeedsActionException or NeedsAuthException ? ex.Message : SafeError(ex))); }
        }
        // Local cleanup is independent of Google availability. Each result keeps its receipt.
        if (job.DeleteFolderRequested && !job.FolderDeleted)
            await Part(JobActions.Download, "Xóa thư mục video đã chọn", () => WithFolderAsync(job, () => source.DeleteFolderAsync(job, () => Save(job), ct), ct));
        if (job.DeleteYouTubeRequested && !job.YouTubeDeleted)
            await Part(JobActions.UploadYouTube, "Xóa video YouTube đã chọn", () => youtube.DeleteVideoAsync(job, () => Save(job), ct));
        if (job.DeleteSheetRequested && !job.SheetDeleted)
            await Part(JobActions.WriteSheet, "Xóa dòng Google Sheets đã chọn", () => WithSheetAsync(job, () => sheets.DeleteSheetAsync(job, () => Save(job), ct), ct));
        if (failures.Count > 0)
        {
            // Report the part that needs action, while preserving all successful deletions.
            var failure = failures.FirstOrDefault(x => x.Error is NeedsAuthException);
            if (failure.Error is null) failure = failures[0];
            job.StageAction = failure.Action;
            throw failure.Error is NeedsAuthException
                ? new NeedsAuthException(string.Join(" ", failures.Select(x => x.Message)))
                : failure.Error;
        }
        var deleted = (job.DeleteFolderRequested ? JobActions.Download : 0) | (job.DeleteSheetRequested ? JobActions.WriteSheet : 0) | (job.DeleteYouTubeRequested ? JobActions.UploadYouTube : 0);
        job.PendingActions = job.RemoveHistoryRequested ? JobActions.None : job.Work & ~deleted;
        job.RemovedFromHistory = job.RemoveHistoryRequested;
    }
}
