namespace TimVideoHay.Core;

public sealed partial class JobCoordinator(IJobStore store, IVideoSource source, ISheetDestination sheets, IYouTubeDestination youtube, TimeSpan? verificationInterval = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? active;
    private Task? worker;
    private bool preserveYouTubeChecks;
    private readonly HashSet<string> pauseRequests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly object sync = new();
    public event Action<VideoJob>? Changed;
    public bool IsRunning { get { lock (sync) return activities.Count > 0 || worker is { IsCompleted: false }; } }

    public async Task InitializeAsync()
    {
        foreach (var job in await store.ListAsync())
        {
            if (job.Actions is null) { job.MigrateWorkflow(); await Save(job); }
            if (job.RemovedFromHistory) continue;
            if (JobHistory.NeedsAttention(job) && job.Issue is null)
            {
                var error = job.State == JobState.NeedsAuth ? (Exception)new NeedsAuthException(job.Message) : new NeedsActionException(job.Message);
                job.Issue = JobIssue.Create(job, error, job.Message); await Save(job);
            }
            try { if (!CanVerifyExisting(job)) { var identity = job.DirectoryIdentity; var videoIdentity = job.VideoIdentity; await source.MigrateOutputStateAsync(job); if (identity != job.DirectoryIdentity || videoIdentity != job.VideoIdentity) await Save(job); } }
            catch (NeedsActionException) { job.Message = "Tệp quản lý cũ không khớp công việc nên chưa được di chuyển. Kiểm tra thư mục trước khi tiếp tục."; await Save(job); }
            if (job.State != JobState.Complete)
            {
                // Restore locally, after legacy ownership migration. Do not rewrite unchanged history rows.
                var previous = (job.Progress, job.ProgressAction, job.DownloadedBytes, job.DownloadTotalBytes, job.UploadedBytes);
                job.RestoreUploadProgress();
                if (job.ProgressAction != JobActions.UploadYouTube)
                    try { await source.RestoreDownloadProgressAsync(job); }
                    catch (Exception ex) when (ex is NeedsActionException or IOException or UnauthorizedAccessException) { }
                if (previous != (job.Progress, job.ProgressAction, job.DownloadedBytes, job.DownloadTotalBytes, job.UploadedBytes)) await Save(job);
            }
            var legacyProcessingWait = job.State == JobState.NeedsAction && job.Message == "Video đã có ID nhưng YouTube chưa xử lý xong. Bấm Tiếp tục sau để xác minh; app không upload lại.";
            if (!job.DeletionRequested && CanVerifyExisting(job) && (legacyProcessingWait || job.State is JobState.WaitingForYouTube or JobState.Running or JobState.Queued))
            {
                job.State = JobState.WaitingForYouTube;
                job.NextYouTubeCheckAt = DateTimeOffset.UtcNow;
                job.Message = "Đã tải lên. App sẽ tự xác nhận video đã đăng; không chờ YouTube xử lý chất lượng.";
                await Save(job);
            }
            else if (job.State is JobState.Running or JobState.Queued or JobState.WaitingForYouTube)
            {
                job.State = JobState.Paused;
                job.Message = "App đã dừng trước đó. Bấm Tiếp tục để kiểm tra checkpoint rồi chạy lại.";
                await Save(job);
            }
        }
        if ((await store.ListAsync()).Any(x => !x.RemovedFromHistory && x.State is JobState.WaitingForYouTube or JobState.WaitingToRetry)) Start();
    }
    public async Task RefreshCoversAsync(string id, CancellationToken ct)
    {
        VideoJob job;
        JobActivity activity;
        await gate.WaitAsync(ct);
        try
        {
            job = (await store.ListAsync(ct)).Single(x => x.Id == id);
            if (job.RemovedFromHistory) throw new NeedsActionException("Công việc đã xóa khỏi danh sách.");
            if (job.Media is null || job.DownloadDirectory is null || !job.CanRefreshCovers) throw new NeedsActionException("Chưa có thư mục video tải hoàn chỉnh hoặc công việc này đang bận.");
            activity = Reserve(job, ct, "covers");
        }
        finally { gate.Release(); }
        await ExecuteActivityAsync(activity, async token =>
        {
            var stage = job.Stage; var progress = job.Progress;
            await Stage(job, "Sửa ảnh bìa nguồn", JobActions.Download);
            try
            {
                job.Media = await WithSourceAsync(() => source.RefreshCoversAsync(job, () => Save(job), token), token);
                job.Message = job.Media.CoverStatus ?? "Đã cập nhật ảnh bìa.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { job.Message = "Đã dừng sửa ảnh bìa; video và kết quả đăng được giữ nguyên."; }
            catch (Exception ex) { job.Message = ex is NeedsActionException ? ex.Message : SafeError(ex); }
            finally { job.Stage = stage; job.Progress = progress; await Save(job); }
        });
    }
    public async Task SubmitAsync(VideoJob job, string? replaceDeletedId = null, CancellationToken ct = default)
    {
        Rules.Validate(job);
        job.Source = await source.ResolveAsync(job.Input, ct);
        var legacySources = new Dictionary<string, SourceVideo>(StringComparer.Ordinal);
        foreach (var old in (await store.ListAsync(ct)).Where(x => x.Source is null))
        {
            try { legacySources[old.Id] = await source.ResolveAsync(old.Input, ct); }
            catch (Exception ex) when (ex is ArgumentException or NeedsActionException or HttpRequestException)
            { throw new NeedsActionException("Chưa nhận diện được link của công việc cũ “" + old.Name + "”. Kiểm tra link cũ trước khi tạo để tránh video trùng."); }
        }
        await gate.WaitAsync(ct);
        try
        {
            lock (sync) { if (stopping) throw new NeedsActionException("Đang dừng ứng dụng; chờ lưu checkpoint rồi thử lại."); }
            var jobs = await store.ListAsync();
            if (jobs.Any(x => x.RequestId == job.RequestId)) return;
            foreach (var old in jobs)
            {
                if (old.Source is null)
                {
                    if (!legacySources.TryGetValue(old.Id, out var resolved)) throw new NeedsActionException("Lịch sử vừa thay đổi; thử thêm công việc lại.");
                    old.Source = resolved;
                }
                if (old.Source.Id != job.Source.Id) continue;
                if (replaceDeletedId is not null && jobs.Any(x => x.Id == replaceDeletedId && x.Source?.Id == job.Source.Id && x.RemovedFromHistory && x.Results == JobActions.None && !x.UnknownResult)
                    && old.RemovedFromHistory && old.Results == JobActions.None && !old.UnknownResult) continue;
                throw new DuplicateJobException(old);
            }
            job.BlockListSheet ??= job.Sheet ?? (await store.SettingsAsync(ct)).Sheet;
            job.BlockListSnapshotCaptured = true;
            job.Actions = job.SelectedActions; job.PendingActions = job.Actions;
            job.EnqueuedAt = DateTimeOffset.UtcNow;
            job.DownloadAttemptId = job.Actions.Value.HasFlag(JobActions.Download) ? Guid.NewGuid().ToString("N") : null;
            job.SheetAttemptId = job.Actions.Value.HasFlag(JobActions.WriteSheet) ? Guid.NewGuid().ToString("N") : null;
            job.YouTubeAttemptId = job.Actions.Value.HasFlag(JobActions.UploadYouTube) ? Guid.NewGuid().ToString("N") : null;
            await store.SaveAsync(job);
        }
        finally { gate.Release(); }
        Changed?.Invoke(job);
        Start();
    }
    public async Task ResumeAsync(string id)
    {
        await gate.WaitAsync();
        try
        {
            var job = (await store.ListAsync()).Single(x => x.Id == id);
            if (job.RemovedFromHistory || pauseRequests.Contains(id) || IsJobBusy(id)) return;
            if (job.State is JobState.Complete or JobState.Running or JobState.Queued or JobState.WaitingForYouTube) return;
            var retryAt = job.Issue?.RetryAt;
            job.State = retryAt > DateTimeOffset.UtcNow ? JobState.WaitingToRetry : JobState.Queued;
            job.NextRetryAt = job.State == JobState.WaitingToRetry ? retryAt : null;
            if (job.State == JobState.Queued) job.Issue = null;
            job.AutomaticRetryAttempts = 0;
            job.EnqueuedAt = DateTimeOffset.UtcNow;
            job.Message = job.State == JobState.WaitingToRetry ? $"Giữ tiến độ; tự thử lại sau {retryAt!.Value.ToLocalTime():HH:mm:ss}." : "";
            await Save(job);
        }
        finally { gate.Release(); }
        Start();
    }
    public Task PauseJobAsync(string id) => ChangeJobAsync(id);
    public async Task<VideoJob> RecheckResultsAsync(string id, CancellationToken ct = default)
    {
        VideoJob job;
        JobActivity activity;
        await gate.WaitAsync(ct);
        try
        {
            job = (await store.ListAsync(ct)).Single(x => x.Id == id);
            if (IsJobBusy(id) || job.State is JobState.Running or JobState.Queued or JobState.WaitingForYouTube or JobState.WaitingToRetry) return job;
            activity = Reserve(job, ct, "inspect");
        }
        finally { gate.Release(); }
        return await ExecuteActivityAsync(activity, async token =>
        {
            ct = token;
            var messages = new List<string>();
            if (job.SheetVerified && !job.SheetDeleted)
                try
                {
                    int? row = null;
                    await WithSheetAsync(job, async () => { row = await sheets.FindExistingRowAsync(job, ct); }, ct);
                    if (row is null) { job.SheetDeleted = true; messages.Add("Dòng video không còn trong tab Sheets gốc; đã cập nhật kết quả bảng."); }
                    else { if (job.SheetRow != row) messages.Add("Đã tìm lại dòng video ở vị trí hiện tại trong Sheets."); job.SheetRow = row; }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is NeedsActionException or NeedsAuthException or HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
                { messages.Add("Chưa kiểm tra được Sheets; giữ trạng thái cũ. " + (ex is NeedsActionException or NeedsAuthException ? ex.Message : "Kiểm tra kết nối Google/mạng.")); }
            if (job.YouTubeId is not null && !job.YouTubeDeleted)
                try
                {
                    if (!await youtube.ExistsAsync(job, ct)) { job.YouTubeDeleted = true; messages.Add("Video không còn trên kênh YouTube gốc; đã tự cập nhật là xóa thủ công, có thể đăng lại trong Bổ sung."); }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is NeedsActionException or NeedsAuthException or HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
                { messages.Add("Chưa kiểm tra được YouTube; giữ kết quả cũ. " + (ex is NeedsActionException or NeedsAuthException ? ex.Message : "Kiểm tra kết nối Google/mạng.")); }
            if (job.DownloadDirectory is not null && !job.FolderDeleted && !Directory.Exists(job.DownloadDirectory))
            {
                try {
                    var roots = (await store.ListAsync(ct)).Where(x => !x.RemovedFromHistory).Select(x => x.Folder).Prepend((await store.SettingsAsync(ct)).CurrentFolder).ToArray();
                    var found = await source.FindMovedFolderAsync(job, roots, () => Save(job), ct);
                    messages.Add(found ?? "Không thấy thư mục cũ: chọn vị trí mới nếu đã chuyển, hoặc xác nhận đã xóa thủ công trong nút Xóa.");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is NeedsActionException or IOException or UnauthorizedAccessException)
                { messages.Add("Chưa tự liên kết được thư mục; chọn vị trí mới để kiểm tra. " + (ex is NeedsActionException ? ex.Message : "Kiểm tra quyền truy cập/file.")); }
            }
            else if (job.MissingDownload) messages.Add("File video đã mất; thư mục/ảnh bìa còn lại được giữ. Có thể chọn Tải trong Bổ sung.");
            if (job.HasDownload && !job.MissingDownload && job.Media!.Covers?.Any(c => !File.Exists(c.Path)) == true)
                messages.Add("Ảnh bìa đã mất; video được giữ. Dùng Bổ sung → Tải lại ảnh bìa để tạo lại.");
            if (messages.Count > 0) job.Message = string.Join(" ", messages);
            await Save(job); return job;
        });
    }
    public async Task<VideoJob> RelocateFolderAsync(string id, string directory, CancellationToken ct = default)
    {
        VideoJob job;
        JobActivity activity;
        await gate.WaitAsync(ct);
        try
        {
            job = (await store.ListAsync(ct)).Single(x => x.Id == id);
            if (job.RemovedFromHistory || job.State is JobState.Running or JobState.Queued or JobState.WaitingForYouTube)
                throw new NeedsActionException("Tạm dừng công việc trước khi liên kết vị trí mới.");
            // A moved destination requires a new deletion choice, not the old queued intent.
            job.DeletionRequested = false; job.DeleteFolderRequested = false; job.DeleteSheetRequested = false;
            job.DeleteYouTubeRequested = false; job.RemoveHistoryRequested = false;
            activity = Reserve(job, ct, "folder");
        }
        finally { gate.Release(); }
        return await ExecuteActivityAsync(activity, async token => { await source.RelocateFolderAsync(job, directory, () => Save(job), token); return job; });
    }
    public async Task RemoveJobAsync(string id, bool deleteSheet = false, bool deleteYouTube = false, bool deleteFolder = false, bool removeHistory = false, bool confirmMissingFolder = false)
    {
        await PauseJobAsync(id);
        JobActivity? activity = null;
        await gate.WaitAsync();
        try
        {
            var job = (await store.ListAsync()).Single(x => x.Id == id);
            if (job.RemovedFromHistory) return;
            job.MigrateWorkflow();
            var chosen = (deleteSheet ? JobActions.WriteSheet : 0) | (deleteYouTube ? JobActions.UploadYouTube : 0) | (deleteFolder ? JobActions.Download : 0);
            if ((chosen & ~job.Results) != 0) throw new NeedsActionException("Kết quả đã thay đổi. Mở lại lựa chọn xóa.");
            if (removeHistory && (job.UnknownResult || (job.Results & ~chosen) != 0)) throw new NeedsActionException("Phải xóa hết kết quả hiện có và xác minh kết quả chưa rõ trước khi xóa lịch sử.");
            if (chosen == JobActions.None && !removeHistory) return;
            if (confirmMissingFolder && (!deleteFolder || job.DownloadDirectory is null || Directory.Exists(job.DownloadDirectory) || File.Exists(job.DownloadDirectory)))
                throw new NeedsActionException("Thư mục hiện có hoặc lựa chọn không còn hợp lệ. Mở lại lựa chọn Xóa.");
            job.FolderAbsenceConfirmed = confirmMissingFolder;
            job.DeletionRequested = true; job.DeleteSheetRequested = deleteSheet; job.DeleteYouTubeRequested = deleteYouTube;
            job.DeleteFolderRequested = deleteFolder;
            job.RemoveHistoryRequested = removeHistory;
            job.NextYouTubeCheckAt = null; job.NextRetryAt = null; job.Issue = null;
            job.State = JobState.Running;
            job.Stage = "Đang xóa kết quả đã chọn"; job.Message = "Đã lưu lựa chọn xóa. App sẽ giữ mục này nếu chưa xóa xong.";
            activity = Reserve(job, CancellationToken.None, "delete");
            await Save(job);
        }
        catch { if (activity is not null) Release(activity); throw; }
        finally { gate.Release(); }
        if (!deleteSheet && !deleteYouTube) await Task.Run(() => ExecuteActivityAsync(activity!, ct => RunAsync(activity!.Job, ct)));
        else Launch(activity!);
    }
    public async Task AddActionsAsync(string id, JobActions additions, AppSettings targets, string niche, bool useCurrentTargets = false)
    {
        await gate.WaitAsync();
        try
        {
            var job = (await store.ListAsync()).Single(x => x.Id == id);
            if (IsJobBusy(id)) throw new NeedsActionException("Công việc này đang có thao tác khác; chờ thao tác đó hoàn tất.");
            job.MigrateWorkflow();
            if (additions == JobActions.None || (additions & ~job.Addable) != 0) throw new NeedsActionException("Tiến trình đã thay đổi hoặc phần này đang dở/đã có kết quả. Mở lại lựa chọn bổ sung.");
            if (additions.HasFlag(JobActions.UploadYouTube) && (!job.HasDownload || job.MissingDownload))
            {
                if (job.Addable.HasFlag(JobActions.Download)) additions |= JobActions.Download;
                else if (!job.Work.HasFlag(JobActions.Download)) throw new NeedsActionException("Cần tải video trước khi đăng.");
            }
            // Prepare a copy: failed validation must not reset a persisted receipt.
            var next = System.Text.Json.JsonSerializer.Deserialize<VideoJob>(System.Text.Json.JsonSerializer.Serialize(job))!;
            void Archive(JobActions action, bool deleted) => next.Receipts.Add(new(action, (action == JobActions.Download ? next.DownloadAttemptId : action == JobActions.WriteSheet ? next.SheetAttemptId : next.YouTubeAttemptId) ?? next.AttemptId, DateTimeOffset.UtcNow, next.DownloadDirectory, next.Media, next.Sheet, next.SheetRow, next.SheetTypeValue, next.Channel, next.YouTubeId, next.UploadSessionKey, next.UploadedBytes, deleted));
            if (additions.HasFlag(JobActions.Download))
            {
                if (next.FolderDeleted) { Archive(JobActions.Download, true); next.Media = null; next.DownloadDirectory = null; next.DirectoryIdentity = null; next.VideoIdentity = null; next.DownloadFormatId = null; next.DownloadExtension = null; next.FolderAbsenceConfirmed = false; }
                if (next.MissingDownload) { Archive(JobActions.Download, false); next.Media = null; next.VideoIdentity = null; } // Preserve owned folder/covers and format; rebuild only the missing media.
                next.Folder = next.DownloadDirectory is not null || next.FolderDeleted && !useCurrentTargets ? next.Folder : targets.CurrentFolder;
                next.FolderDeleted = false; next.FolderDeleteStarted = false; next.DownloadAttemptId = Guid.NewGuid().ToString("N");
                next.DownloadedBytes = 0; next.DownloadTotalBytes = 0;
            }
            if (additions.HasFlag(JobActions.WriteSheet))
            {
                if (next.SheetDeleted) Archive(JobActions.WriteSheet, true);
                next.Sheet = next.SheetDeleted && !useCurrentTargets ? next.Sheet : targets.Sheet;
                if (!next.SheetDeleted || useCurrentTargets) next.ShortMaxMilliseconds = targets.ShortMaxMilliseconds;
                next.Niche = niche; next.SheetDeleted = false; next.SheetDeleteStarted = false; next.SheetWriteStarted = false; next.SheetVerified = false; next.SheetRow = null; next.SheetTypeValue = null; next.Metadata = null;
                next.SheetAttemptId = Guid.NewGuid().ToString("N");
                next.SourceChannel = null; next.SourceChannelSheet = null; next.SourceChannelUpdated = false; next.IncludeSourceChannel = true;
            }
            if (additions.HasFlag(JobActions.UploadYouTube))
            {
                if (next.YouTubeDeleted) Archive(JobActions.UploadYouTube, true);
                next.Channel = next.YouTubeDeleted && !useCurrentTargets ? next.Channel : targets.Channel;
                next.YouTubeDeleted = false; next.YouTubeDeleteStarted = false; next.YouTubeVerified = false; next.YouTubeId = null; next.MediaSendStarted = false; next.UploadSessionKey = null; next.UploadedBytes = 0;
                next.NextYouTubeCheckAt = null; next.YouTubeCheckAttempts = 0; next.YouTubeVerificationStartedAt = null; next.Privacy = targets.Privacy;
                next.YouTubeAttemptId = Guid.NewGuid().ToString("N");
            }
            next.Actions = job.SelectedActions | additions | (additions.HasFlag(JobActions.UploadYouTube) ? JobActions.Download : 0);
            next.PendingActions = job.Work | additions; next.AttemptId = Guid.NewGuid().ToString("N");
            if (job.Work == JobActions.None) { next.Progress = 0; next.ProgressAction = JobActions.None; }
            Rules.Validate(next); next.State = JobState.Queued; next.EnqueuedAt = DateTimeOffset.UtcNow; next.Message = "Tiếp tục / thực hiện: " + VideoJob.ActionLabel(next.Work); next.Stage = "Chờ thực hiện phần bổ sung";
            await Save(next);
        }
        finally { gate.Release(); }
        Start();
    }
    private async Task ChangeJobAsync(string id)
    {
        Task? finishing = null;
        await gate.WaitAsync();
        try
        {
            var job = (await store.ListAsync()).Single(x => x.Id == id);
            if (job.RemovedFromHistory) return;
            pauseRequests.Add(id);
            lock (sync)
            {
                if (activities.TryGetValue(id, out var activity))
                {
                    activity.Cancellation.Cancel();
                    finishing = activity.Finished.Task;
                }
            }
        }
        finally { gate.Release(); }
        // Wait for this job's final checkpoint, while other jobs keep their own tokens.
        if (finishing is not null) await finishing;
        await gate.WaitAsync();
        try
        {
            var job = (await store.ListAsync()).Single(x => x.Id == id);
            if (job.State != JobState.Complete)
            {
                job.State = JobState.Paused; job.NextYouTubeCheckAt = null; job.NextRetryAt = null;
                job.Message = "Đã tạm dừng công việc này. Bấm Tiếp tục để chạy lại.";
            }
            await Save(job);
        }
        finally { pauseRequests.Remove(id); gate.Release(); }
        lock (sync) { if (wake.CurrentCount == 0) wake.Release(); }
    }
    private void Start()
    {
        lock (sync)
        {
            if (stopping) return;
            if (worker is { IsCompleted: false }) { if (wake.CurrentCount == 0) wake.Release(); return; }
            preserveYouTubeChecks = false;
            active?.Dispose();
            active = new CancellationTokenSource();
            var token = active.Token;
            worker = Task.Run(() => DrainAsync(token));
        }
    }
    public async Task StopAsync(bool preservePendingYouTube = false)
    {
        Task? pending;
        Task[] operations;
        await gate.WaitAsync();
        try
        {
            lock (sync)
            {
                stopping = true; preserveYouTubeChecks = preservePendingYouTube; active?.Cancel(); pending = worker;
                var current = activities.Values.ToArray();
                operations = current.Select(x => x.Finished.Task).ToArray();
                foreach (var activity in current) activity.Cancellation.Cancel();
            }
        }
        finally { gate.Release(); }
        if (pending is not null) await pending;
        await Task.WhenAll(operations);
        await gate.WaitAsync();
        try
        {
            foreach (var job in await store.ListAsync())
                if (!job.RemovedFromHistory && (job.State == JobState.Queued || !preservePendingYouTube && job.State is JobState.WaitingForYouTube or JobState.WaitingToRetry))
                { job.State = JobState.Paused; job.Message = "Đã dừng. Bấm Tiếp tục để chạy lại."; await Save(job); }
        }
        finally { lock (sync) stopping = false; gate.Release(); }
    }
    private async Task Save(VideoJob job) { await store.SaveAsync(job); Changed?.Invoke(job); }
    private async Task Stage(VideoJob job, string text, JobActions action = JobActions.None) { job.Stage = text; job.StageAction = action; await Save(job); }
    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                VideoJob? job;
                JobActivity? activity = null;
                TimeSpan? wait = null;
                await gate.WaitAsync(ct);
                try
                {
                    var allJobs = (await store.ListAsync(ct)).Where(x => !x.RemovedFromHistory).ToArray();
                    var jobs = allJobs.Where(x => !pauseRequests.Contains(x.Id) && !IsJobBusy(x.Id)).ToArray();
                    var now = DateTimeOffset.UtcNow;
                    var queued = JobQueue.Order(jobs).Where(CanDispatch).ToArray();
                    job = queued.FirstOrDefault(x => x.DeletionRequested)
                        ?? jobs.FirstOrDefault(x => CanDispatch(x) && x.State == JobState.WaitingForYouTube && (x.NextYouTubeCheckAt ?? now) <= now)
                        ?? jobs.FirstOrDefault(x => CanDispatch(x) && x.State == JobState.WaitingToRetry && (x.NextRetryAt ?? now) <= now)
                        ?? queued.FirstOrDefault();
                    if (job is null)
                    {
                        var next = jobs.Where(x => CanDispatch(x) && x.State is JobState.WaitingForYouTube or JobState.WaitingToRetry).Select(x => x.State == JobState.WaitingToRetry ? x.NextRetryAt ?? now : x.NextYouTubeCheckAt ?? now).Order().FirstOrDefault();
                        if (next == default) { lock (sync) { worker = null; } return; }
                        wait = next - now;
                    }
                    else
                    {
                        if (!job.DeletionRequested && BlockingAuthentication(job, allJobs) is { } blocked)
                        {
                            job.Issue = blocked with { Id = Guid.NewGuid().ToString("N"), Created = now };
                            job.State = JobState.NeedsAuth; job.Message = "Cùng kết nối Google đang cần đăng nhập lại. Một lần kết nối lại sẽ khôi phục các công việc liên quan.";
                            job.Stage = "Chờ khôi phục kết nối Google"; await Save(job); continue;
                        }
                        if (!job.DeletionRequested && BlockingRetry(job, allJobs, now) is { } limited)
                        {
                            job.Issue = limited with { Id = Guid.NewGuid().ToString("N"), AffectedActions = job.Work, Created = now };
                            job.State = JobState.WaitingToRetry; job.NextRetryAt = limited.RetryAt;
                            job.Message = $"Cùng dịch vụ đang giới hạn truy cập. Tự thử lại sau {limited.RetryAt!.Value.ToLocalTime():HH:mm:ss}.";
                            await Save(job); continue;
                        }
                        job.State = JobState.Running; job.NextRetryAt = null; job.Issue = null; await Save(job);
                        activity = Reserve(job, ct, job.DeletionRequested ? "delete" : CanVerifyExisting(job) ? "verify" : IsMediaWork(job) ? "work" : "sheet");
                    }
                }
                finally { gate.Release(); }
                if (job is null) await wake.WaitAsync(wait!.Value > TimeSpan.Zero ? wait.Value : TimeSpan.Zero, ct);
                else
                {
                    Launch(activity!);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private async Task RunAsync(VideoJob job, CancellationToken ct)
    {
        try
        {
            if (job.DownloadDirectory is not null && !job.FolderDeleted && !job.FolderAbsenceConfirmed && !Directory.Exists(job.DownloadDirectory)
                && (job.DeletionRequested ? job.DeleteFolderRequested : job.Work.HasFlag(JobActions.Download) || job.Work.HasFlag(JobActions.UploadYouTube)))
            {
                await Stage(job, "Tìm lại thư mục đã chuyển");
                var roots = (await store.ListAsync(ct)).Where(x => !x.RemovedFromHistory).Select(x => x.Folder).Prepend((await store.SettingsAsync(ct)).CurrentFolder).ToArray();
                var found = await source.FindMovedFolderAsync(job, roots, () => Save(job), ct);
                if (found is not null) job.Message = found;
                await Save(job);
            }
            if (job.DeletionRequested)
            {
                await DeleteResultsAsync(job, ct);
            }
            else if (CanVerifyExisting(job))
            {
                // A known uploaded video only needs a read. Do not replay Sheets,
                // hash a large local file or require the local media to remain present.
                await youtube.UploadAndVerifyAsync(job, () => Save(job), new DirectProgress<double>(_ => { }), ct);
                job.YouTubeVerified = true; job.PendingActions = job.Work & ~JobActions.UploadYouTube;
                await Save(job);
                if (job.Work != JobActions.None) { job.State = JobState.Queued; await Save(job); return; }
            }
            else
            {
                job.PendingActions ??= job.Work;
                if (job.Source is null)
                {
                    await Stage(job, "Nhận diện Douyin"); job.Source = await source.ResolveAsync(job.Input, ct); await Save(job);
                }
                await Stage(job, "Kiểm tra kênh block");
                if (!job.BlockListSnapshotCaptured)
                {
                    job.BlockListSheet ??= job.Sheet ?? (await store.SettingsAsync(ct)).Sheet;
                    job.BlockListSnapshotCaptured = true;
                    await Save(job);
                }
                var blockTarget = job.BlockListSheet ?? job.Sheet;
                if (blockTarget is null)
                    job.BlockListMessage = "Chưa kết nối bảng nên chưa kiểm tra Kênh block";
                else
                {
                    job.BlockListSheet ??= blockTarget;
                    job.BlockListMessage = "";
                    if (blockTarget.BlockedChannelSheetId is null || blockTarget.BlockedChannelSheetId == blockTarget.SheetId
                        || blockTarget.BlockedChannelSheetId == blockTarget.SourceChannelSheetId)
                        throw new NeedsActionException("Chọn tab Kênh block trong Kết nối & cài đặt", JobIssueKind.Configuration);
                    if (!string.IsNullOrWhiteSpace(job.SourceChannel?.SecUid)) job.SourceAuthorSecUid = job.SourceChannel.SecUid;
                    else if (string.IsNullOrWhiteSpace(job.SourceAuthorSecUid))
                        job.SourceAuthorSecUid = await WithSourceAsync(() => source.ReadAuthorSecUidAsync(job.Source, ct), ct);
                    if (string.IsNullOrWhiteSpace(job.SourceAuthorSecUid))
                        throw new NeedsActionException("Không lấy được kênh của đúng video Douyin.", JobIssueKind.SourceUnavailable);
                    await Save(job);
                    await sheets.CheckBlockedChannelAsync(job, ct);
                }
                await Save(job);
                // Check every selected destination before any download or remote mutation.
                if (job.Work.HasFlag(JobActions.Download)) { await Stage(job, "Kiểm tra thư mục trùng tên", JobActions.Download); await source.CheckDuplicateAsync(job, ct); }
                if (job.Work.HasFlag(JobActions.WriteSheet)) { await Stage(job, "Kiểm tra video trùng trong bảng", JobActions.WriteSheet); await sheets.CheckDuplicateAsync(job, ct); await sheets.PreflightAsync(job.Sheet!, ct); }
                if (job.Work.HasFlag(JobActions.UploadYouTube)) { await Stage(job, "Kiểm tra tiêu đề trùng trên YouTube", JobActions.UploadYouTube); await youtube.CheckDuplicateAsync(job, ct); await youtube.PreflightAsync(job.Channel!, ct); }
                if (job.Work.HasFlag(JobActions.WriteSheet) && job.Metadata is null)
                {
                    await Stage(job, "Lấy thời lượng Douyin", JobActions.WriteSheet); job.Metadata = await WithSourceAsync(() => source.ReadMetadataAsync(job.Source, ct), ct);
                    await Save(job);
                }
                if (job.Work.HasFlag(JobActions.WriteSheet))
                {
                    if (job.Metadata?.SourceId != job.Source.Id) throw new NeedsActionException("Thời lượng không thuộc đúng video.");
                    _ = Rules.VideoTypeMilliseconds(job.Metadata.DurationMilliseconds);
                    await Stage(job, "Lấy dữ liệu kênh nguồn và dịch tên", JobActions.WriteSheet); await WithSourceAsync(() => sheets.PrepareAsync(job, () => Save(job), ct), ct);
                }
                var progress = new DirectProgress<double>(p => { job.Progress = Math.Clamp(p, 0, 100); Changed?.Invoke(job); });
                var validatedDownload = false;
                if (job.Work.HasFlag(JobActions.Download))
                {
                    if (job.Media is null || job.FolderDeleted || (job.DownloadDirectory is not null && job.Media.CoverStatus is null))
                    {
                        job.ProgressAction = JobActions.Download;
                        job.Progress = job.DownloadTotalBytes > 0 ? job.DisplayProgress : job.Progress;
                        await Stage(job, "Chọn chất lượng và tải video, ảnh bìa", JobActions.Download);
                        job.Media = await WithSourceAsync(() => source.DownloadAsync(job, () => Save(job), progress, ct), ct); await Save(job);
                    }
                    await source.ValidateFileAsync(job.Media, ct);
                    validatedDownload = true;
                    job.PendingActions = job.Work & ~JobActions.Download; await Save(job);
                }
                if (job.Work.HasFlag(JobActions.WriteSheet))
                {
                    await Stage(job, "Ghi và xác minh Google Sheets", JobActions.WriteSheet); await WithSheetAsync(job, () => sheets.WriteAndVerifyAsync(job, () => Save(job), ct), ct);
                    job.PendingActions = job.Work & ~JobActions.WriteSheet; await Save(job);
                }
                if (job.Work.HasFlag(JobActions.UploadYouTube))
                {
                    if (job.Media is null || job.FolderDeleted) throw new NeedsActionException("File đã tải không còn. Tải lại video trước khi đăng.");
                    if (!validatedDownload) await source.ValidateFileAsync(job.Media, ct);
                    job.ProgressAction = JobActions.UploadYouTube;
                    job.Progress = Math.Clamp(100d * job.UploadedBytes / job.Media.Length, 0, 100);
                    await Stage(job, "Đăng và xác minh YouTube", JobActions.UploadYouTube); await youtube.UploadAndVerifyAsync(job, () => Save(job), progress, ct);
                    job.YouTubeVerified = true; job.PendingActions = job.Work & ~JobActions.UploadYouTube; await Save(job);
                }
            }
            job.State = JobState.Complete; job.Issue = null; job.NextRetryAt = null; job.AutomaticRetryAttempts = 0;
            job.NextYouTubeCheckAt = null;
            job.Stage = "Hoàn tất";
            job.Message = "Đã hoàn tất các phần đã chọn. " + job.ResultsLabel + " " + job.SourceChannelMessage + " " + job.BlockListMessage + (job.OmittedActions == JobActions.None ? "" : " Đã bỏ phần chưa hoàn tất: " + VideoJob.ActionLabel(job.OmittedActions) + ".");
            if (job.DeletionRequested)
            {
                job.DeletionRequested = false; job.DeleteSheetRequested = false; job.DeleteYouTubeRequested = false; job.DeleteFolderRequested = false;
                job.Stage = job.RemovedFromHistory ? "Đã xóa công việc" : "Đã xóa kết quả đã chọn";
                job.Message = job.RemovedFromHistory ? "Đã xóa hết kết quả và công việc khỏi lịch sử." : "Kết quả khác được giữ. Dùng Bổ sung / thực hiện lại để chạy phần đã xóa.";
                if (!job.RemovedFromHistory && job.Work != JobActions.None) { job.State = JobState.Paused; job.Message += " Phần còn dở được giữ checkpoint: " + VideoJob.ActionLabel(job.Work) + "."; }
            }
            if (job.State == JobState.Complete) job.Progress = 100;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!job.DeletionRequested && preserveYouTubeChecks && CanVerifyExisting(job)) await WaitForYouTube(job, "Đã lưu; tự kiểm tra YouTube khi mở app lại.");
            else { job.State = JobState.Paused; job.Message = "Đã lưu checkpoint. Tiếp tục sẽ kiểm tra kết quả trước khi gửi lại."; }
        }
        catch (YouTubePendingException ex) when (CanVerifyExisting(job)) { await WaitForYouTube(job, ex.Message); }
        catch (HttpRequestException) when (CanVerifyExisting(job)) { await WaitForYouTube(job, "Chưa kết nối được YouTube. App sẽ tự thử lại."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && CanVerifyExisting(job)) { await WaitForYouTube(job, "YouTube phản hồi chậm. App sẽ tự thử lại."); }
        catch (RetryLaterException ex) { ScheduleRetry(job, ex, ex.Message); }
        catch (HttpRequestException ex) { ScheduleRetry(job, ex, SafeError(ex)); }
        catch (OperationCanceledException ex) { ScheduleRetry(job, ex, "Dịch vụ phản hồi chậm."); }
        catch (NeedsAuthException ex) { SetIssue(job, ex, ex.Message); }
        catch (NeedsActionException ex) { SetIssue(job, ex, ex.Message); }
        catch (Exception ex) { SetIssue(job, ex, SafeError(ex)); }
        if (job.BlockListMessage.Length > 0 && !job.Message.Contains(job.BlockListMessage, StringComparison.Ordinal))
            job.Message += " " + job.BlockListMessage;
        await Save(job);
    }
    private static bool CanVerifyExisting(VideoJob job) => !job.DeletionRequested && job.Work.HasFlag(JobActions.UploadYouTube) && !job.YouTubeDeleted && !job.YouTubeVerified && job.YouTubeId is not null && job.Channel is not null;
    private Task WaitForYouTube(VideoJob job, string message)
    {
        job.YouTubeCheckAttempts = Math.Clamp(job.YouTubeCheckAttempts, 0, 15) + 1;
        var delay = verificationInterval ?? TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, Math.Min(4, job.YouTubeCheckAttempts - 1))));
        job.State = JobState.WaitingForYouTube;
        job.Stage = "Tự xác nhận video đã đăng YouTube";
        job.NextYouTubeCheckAt = DateTimeOffset.UtcNow + delay;
        job.Message = message + $" Tự kiểm tra lại lúc {job.NextYouTubeCheckAt.Value.ToLocalTime():HH:mm:ss}. Không upload lại.";
        return Task.CompletedTask;
    }
    private static string SafeError(Exception ex) => ex switch
    {
        IOException => "Lỗi đọc/ghi file. Kiểm tra dung lượng, quyền truy cập và thư mục tải.",
        HttpRequestException => "Kết nối mạng bị gián đoạn. App giữ tiến độ và kiểm tra checkpoint trước khi thử lại.",
        _ => "Có lỗi khi xử lý công việc. Đã lưu checkpoint; thử lại hoặc kiểm tra bản cài. Loại lỗi: " + ex.GetType().Name
    };
}
public sealed class DirectProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
