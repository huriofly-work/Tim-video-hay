namespace TimVideoHay.Core;

public sealed partial class JobCoordinator
{
    public async Task<VideoJob?> AcknowledgeAsync(JobAttention request, AttentionAction action)
    {
        await gate.WaitAsync();
        try
        {
            var job = (await store.ListAsync()).SingleOrDefault(x => x.Id == request.JobId);
            if (job is not null && IsJobBusy(job.Id)) throw new NeedsActionException("Công việc đang được kiểm tra; chờ thao tác đó hoàn tất.");
            var current = job is null ? null : JobAttention.From(job);
            if (current is null) return null;
            if (request.Signature != current.Signature || current.Choices.All(x => x.Action != action))
                throw new NeedsActionException("Thông tin công việc đã thay đổi. Chọn thao tác trên thông báo mới nhất.");
            job!.Decisions.Add(new(request.IssueId ?? "legacy", action, DateTimeOffset.UtcNow));
            if (job.Decisions.Count > 50) job.Decisions.RemoveRange(0, job.Decisions.Count - 50);
            await Save(job); return job;
        }
        finally { gate.Release(); }
    }
    public async Task OmitAffectedAsync(string id)
    {
        await gate.WaitAsync();
        try
        {
            var job = (await store.ListAsync()).Single(x => x.Id == id);
            if (IsJobBusy(id)) throw new NeedsActionException("Công việc đang được kiểm tra; chờ thao tác đó hoàn tất.");
            if (!JobHistory.NeedsAttention(job)) throw new NeedsActionException("Công việc không còn chờ lựa chọn.");
            var omitted = JobAttention.SkippableActions(job);
            if (omitted == JobActions.None) throw new NeedsActionException("Phần này đã gửi hoặc chưa rõ kết quả; kiểm tra kết quả trước.");
            job.PendingActions = job.Work & ~omitted; job.Actions = job.SelectedActions & ~omitted;
            job.OmittedActions |= omitted; job.Issue = null; job.NextRetryAt = null;
            job.State = job.Work == JobActions.None ? JobState.Complete : JobState.Queued;
            job.EnqueuedAt = DateTimeOffset.UtcNow;
            job.Stage = job.Work == JobActions.None ? "Đã xử lý lựa chọn" : "Chờ thực hiện phần còn lại";
            job.Message = "Bạn đã bỏ phần chưa hoàn tất: " + VideoJob.ActionLabel(omitted) + ". Kết quả đã có được giữ nguyên.";
            if (job.State == JobState.Complete) job.Progress = 100;
            await Save(job);
        }
        finally { gate.Release(); }
        Start();
    }
    public async Task<bool> InspectRemoteAsync(string id, CancellationToken ct)
    {
        VideoJob job;
        JobActivity activity;
        await gate.WaitAsync(ct);
        try
        {
            job = (await store.ListAsync(ct)).Single(x => x.Id == id);
            if (!JobHistory.NeedsAttention(job)) return false;
            activity = Reserve(job, ct, "inspect");
        }
        finally { gate.Release(); }
        return await ExecuteActivityAsync(activity, async token =>
        {
            ct = token;
            var issueId = job.Issue?.Id;
            async Task SaveInspection()
            {
                await gate.WaitAsync(ct);
                try
                {
                    var latest = (await store.ListAsync(ct)).Single(x => x.Id == id);
                    if (!JobHistory.NeedsAttention(latest) || latest.Issue?.Id != issueId)
                        throw new NeedsActionException("Công việc đã thay đổi trong lúc kiểm tra; giữ trạng thái mới nhất.");
                    await Save(job);
                }
                finally { gate.Release(); }
            }
            if (job.YouTubeId is not null) return true;
            if (job.UnknownUpload) return await youtube.InspectPendingUploadAsync(job, SaveInspection, ct);
            if (job.UnknownSheet)
            {
                int? row = null;
                await WithSheetAsync(job, async () => { row = await sheets.FindExistingRowAsync(job, ct); }, ct);
                if (row is null) return false;
                job.SheetRow = row; await SaveInspection(); return true;
            }
            return false;
        });
    }
    public async Task AttachUploadedVideoAsync(string id, string videoId, CancellationToken ct)
    {
        VideoJob job;
        JobActivity activity;
        await gate.WaitAsync(ct);
        try
        {
            job = (await store.ListAsync(ct)).Single(x => x.Id == id);
            if (!job.UnknownUpload || !JobHistory.NeedsAttention(job) || !System.Text.RegularExpressions.Regex.IsMatch(videoId, "^[A-Za-z0-9_-]{11}$"))
                throw new NeedsActionException("Kết quả công việc đã thay đổi hoặc ID video không hợp lệ.");
            activity = Reserve(job, ct, "inspect");
        }
        finally { gate.Release(); }
        await ExecuteActivityAsync(activity, async token =>
        {
            ct = token;
            var issueId = job.Issue?.Id; job.YouTubeId = videoId;
            if (!await youtube.ExistsAsync(job, ct)) throw new NeedsActionException("Không tìm thấy video trên đúng kênh gốc. Kiểm tra link và tài khoản trước khi liên kết.");
            await gate.WaitAsync(ct);
            try
            {
                var latest = (await store.ListAsync(ct)).Single(x => x.Id == id);
                if (latest.Issue?.Id != issueId || !latest.UnknownUpload || !JobHistory.NeedsAttention(latest))
                    throw new NeedsActionException("Công việc đã thay đổi trong lúc kiểm tra video.");
                job.Decisions.Add(new(issueId ?? "legacy", AttentionAction.LinkExistingVideo, DateTimeOffset.UtcNow));
                if (job.Decisions.Count > 50) job.Decisions.RemoveRange(0, job.Decisions.Count - 50);
                job.RestoreUploadProgress(); await Save(job);
            }
            finally { gate.Release(); }
        });
    }
    public async Task ConfirmUploadAbsentAsync(string id, string? issueId)
    {
        await gate.WaitAsync();
        try
        {
            var job = (await store.ListAsync()).Single(x => x.Id == id);
            if (IsJobBusy(id)) throw new NeedsActionException("Công việc đang được kiểm tra; chờ thao tác đó hoàn tất.");
            if (!job.UnknownUpload || !JobHistory.NeedsAttention(job) || job.Issue?.Id != issueId)
                throw new NeedsActionException("Công việc đã thay đổi. Kiểm tra thông báo mới trước khi xác nhận chưa có video.");
            job.Decisions.Add(new(issueId ?? "legacy", AttentionAction.ConfirmUploadAbsent, DateTimeOffset.UtcNow));
            if (job.Decisions.Count > 50) job.Decisions.RemoveRange(0, job.Decisions.Count - 50);
            job.UploadSessionKey = null; job.MediaSendStarted = false; job.UploadedBytes = 0; job.Progress = 0;
            await Save(job);
        }
        finally { gate.Release(); }
    }
    public async Task ReconnectDependentJobsAsync(VideoJob original, VideoJob reconnected)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var job in await store.ListAsync())
            {
                if (job.RemovedFromHistory || IsJobBusy(job.Id) || job.State != JobState.NeedsAuth || original.Issue?.DependencyKey is null
                    || job.Issue?.DependencyKey != original.Issue.DependencyKey) continue;
                var changed = false;
                if (original.Sheet is { } oldSheet && reconnected.Sheet is { } newSheet
                    && job.Sheet is { } target && target.CredentialId == oldSheet.CredentialId && target.SpreadsheetId == newSheet.SpreadsheetId)
                {
                    job.Sheet = target with { CredentialId = newSheet.CredentialId };
                    if (job.SourceChannelSheet is not null) job.SourceChannelSheet = job.SourceChannelSheet with { CredentialId = newSheet.CredentialId };
                    changed = true;
                }
                if (original.Channel is { } oldChannel && reconnected.Channel is { } newChannel
                    && job.Channel is { } channel && channel.CredentialId == oldChannel.CredentialId && channel.ChannelId == newChannel.ChannelId)
                { job.Channel = channel with { CredentialId = newChannel.CredentialId }; changed = true; }
                // Primary job was saved by the reconnect UI; its original target is validated there.
                if (job.Id == original.Id) changed = true;
                if (!changed) continue;
                job.Decisions.Add(new(job.Issue.Id, AttentionAction.ReconnectGoogle, DateTimeOffset.UtcNow));
                if (job.Decisions.Count > 50) job.Decisions.RemoveRange(0, job.Decisions.Count - 50);
                job.Issue = null; job.NextRetryAt = null; job.AutomaticRetryAttempts = 0;
                job.State = JobState.Queued; job.EnqueuedAt = DateTimeOffset.UtcNow;
                job.Message = "Đã khôi phục kết nối Google; tiếp tục từ checkpoint."; await Save(job);
            }
        }
        finally { gate.Release(); }
        Start();
    }
    private static JobIssue? BlockingAuthentication(VideoJob job, IEnumerable<VideoJob> jobs)
    {
        var actions = job.DeletionRequested
            ? (job.DeleteSheetRequested && !job.SheetDeleted ? JobActions.WriteSheet : 0) | (job.DeleteYouTubeRequested && !job.YouTubeDeleted ? JobActions.UploadYouTube : 0)
            : job.Work;
        return jobs.Where(x => x.State == JobState.NeedsAuth && x.Issue?.Kind == JobIssueKind.Authentication).Select(x => x.Issue!)
            .FirstOrDefault(issue => new[] { JobActions.WriteSheet, JobActions.UploadYouTube }.Any(action => actions.HasFlag(action)
                && issue.DependencyKey is not null && issue.DependencyKey == JobIssue.CredentialKey(job, action)));
    }
    private static JobIssue? BlockingRetry(VideoJob job, IEnumerable<VideoJob> jobs, DateTimeOffset now) => jobs
        .Where(x => x.Issue?.RetryAt > now && x.Issue.DependencyKey is not null).Select(x => x.Issue!)
        .FirstOrDefault(issue => issue.DependencyKey switch
        {
            "douyin-cdn" => job.Work.HasFlag(JobActions.Download) && (job.Media is null || job.FolderDeleted),
            "douyin-metadata" => job.Work.HasFlag(JobActions.Download) && (job.Media is null || job.FolderDeleted)
                || job.Work.HasFlag(JobActions.WriteSheet) && (job.Metadata is null || job.SourceChannel is null),
            "google-api" => ((job.Work & (JobActions.WriteSheet | JobActions.UploadYouTube)) != 0) || job.DeletionRequested && (job.DeleteSheetRequested || job.DeleteYouTubeRequested),
            _ => false
        });
    private void SetIssue(VideoJob job, Exception error, string message)
    {
        job.Issue = JobIssue.Create(job, error, message); job.Message = message;
        job.NextRetryAt = null;
        job.State = error is NeedsAuthException ? JobState.NeedsAuth : JobState.NeedsAction;
    }
    private void ScheduleRetry(VideoJob job, Exception error, string message)
    {
        job.AutomaticRetryAttempts++;
        SetIssue(job, error, message);
        // A user decision is needed after bounded automatic attempts. Never hammer a failing service.
        if (job.AutomaticRetryAttempts > 2 || job.UnknownResult) return;
        job.NextRetryAt = job.Issue?.RetryAt ?? DateTimeOffset.UtcNow.AddSeconds(15 * job.AutomaticRetryAttempts);
        if (job.NextRetryAt < DateTimeOffset.UtcNow.AddSeconds(1)) job.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(1);
        job.State = JobState.WaitingToRetry;
        job.Message = message + $" Tự thử lại lúc {job.NextRetryAt.Value.ToLocalTime():HH:mm:ss}. Tiến độ đã được lưu.";
    }
}
