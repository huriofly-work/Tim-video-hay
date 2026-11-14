namespace TimVideoHay.Core;

public enum AttentionAction { Retry, ReconnectGoogle, Reconcile, LocateFolder, Pause, OmitAffected, Inspect, ConfirmUploadAbsent, LinkExistingVideo, OpenSettings, ReloadNiches, ResolveNotice, OpenDataDirectory }
public sealed record AttentionChoice(AttentionAction Action, string Label);
public sealed record JobAttention(string JobId, string Name, string Stage, string Message, IReadOnlyList<AttentionChoice> Choices)
{
    public string? OperationId { get; init; }
    public string Guidance { get; init; } = "";
    public int Occurrences { get; init; } = 1;
    public string? IssueId { get; init; }
    public string? DependencyKey { get; init; }
    public double Progress { get; init; }
    public string Results { get; init; } = "";
    public IReadOnlyList<string> RelatedJobIds { get; init; } = [];
    public string Signature => string.Join("\n", JobId, IssueId, Stage, Message, string.Join(",", Choices.Select(x => x.Action)));
    public static JobAttention? From(VideoJob job)
    {
        if (job.RemovedFromHistory || !JobHistory.NeedsAttention(job)) return null;
        var choices = new List<AttentionChoice>();
        if (job.NeedsGoogleReconnect) choices.Add(new(AttentionAction.ReconnectGoogle, "Đăng nhập lại và tiếp tục"));
        else if (job.UnknownResult || job.Issue?.Kind == JobIssueKind.UnknownResult) choices.Add(new(AttentionAction.Reconcile, "Kiểm tra kết quả đã gửi…"));
        else if (job.Issue?.Kind is not (JobIssueKind.InvalidData or JobIssueKind.MissingFolder))
            choices.Add(new(AttentionAction.Retry, job.Issue?.Kind switch {
                JobIssueKind.Duplicate => "Đã xử lý trùng, kiểm tra lại",
                JobIssueKind.Configuration => "Đã sửa cấu hình, kiểm tra lại",
                JobIssueKind.FileAccess => "Đã sửa quyền/lưu trữ, kiểm tra lại",
                JobIssueKind.SourceUnavailable => "Kiểm tra lại nguồn Douyin",
                JobIssueKind.RateLimited => "Chờ và tự thử lại", _ => "Thử lại từ tiến độ đã lưu" }));
        if (job.DownloadDirectory is not null && !job.FolderDeleted && !Directory.Exists(job.DownloadDirectory))
            choices.Add(new(AttentionAction.LocateFolder, "Chọn thư mục đã chuyển…"));
        if (SkippableActions(job) is var skippable && skippable != JobActions.None)
            choices.Add(new(AttentionAction.OmitAffected, "Bỏ phần còn lại: " + VideoJob.ActionLabel(skippable)));
        choices.Add(new(AttentionAction.Inspect, "Mở công việc để kiểm tra"));
        choices.Add(new(AttentionAction.Pause, "Tạm dừng công việc"));
        return new(job.Id, job.Name, job.Stage, job.Message, choices) { IssueId = job.Issue?.Id, DependencyKey = job.Issue?.DependencyKey, Progress = job.DisplayProgress, Results = job.ResultsLabel, Guidance = job.Issue?.Kind switch {
            JobIssueKind.Duplicate => "Đích đã có video hoặc thư mục trùng. Kiểm tra kết quả hiện có trước; chỉ kiểm tra lại sau khi bạn đã xử lý trùng.",
            JobIssueKind.InvalidData => "Dữ liệu không hợp lệ. Mở chi tiết để kiểm tra; không tự lặp lại thao tác với cùng dữ liệu sai.",
            JobIssueKind.Configuration => "Kiểm tra cấu hình và đích gốc của công việc. Đổi cài đặt mặc định chỉ áp dụng cho công việc mới.",
            JobIssueKind.FileAccess => "Kiểm tra dung lượng trống, quyền ghi và ứng dụng đang khóa file; sau đó kiểm tra lại.",
            JobIssueKind.MissingFolder => "Liên kết thư mục đã chuyển để đối chiếu đúng file; không tự tạo lại hoặc xóa ở vị trí không xác định.",
            JobIssueKind.RateLimited => "App giữ thời điểm được phép thử lại; bấm tiếp tục không bỏ qua khoảng chờ.",
            _ => "Tiến độ và kết quả đã có được giữ lại. Chọn cách xử lý bên dưới." } };
    }
    public static JobActions SkippableActions(VideoJob job)
    {
        if (job.DeletionRequested) return JobActions.None;
        var actions = (job.Issue?.AffectedActions ?? job.Work) & job.Work;
        if (job.UnknownSheet) actions &= ~JobActions.WriteSheet;
        if (job.UnknownUpload || job.YouTubeId is not null && !job.YouTubeVerified && !job.YouTubeDeleted) actions &= ~JobActions.UploadYouTube;
        if (actions.HasFlag(JobActions.Download) && job.Work.HasFlag(JobActions.UploadYouTube))
        {
            if (job.UnknownUpload) actions &= ~JobActions.Download;
            else actions |= JobActions.UploadYouTube;
        }
        return actions;
    }
}

// Notifications are derived from persisted job states, independently of history filters.
public sealed class JobAttentionInbox
{
    private readonly Dictionary<string, JobAttention> pending = new(StringComparer.Ordinal);
    public IReadOnlyList<JobAttention> Items => pending.Values.GroupBy(x => x.DependencyKey ?? x.JobId, StringComparer.Ordinal).Select(group =>
    {
        var first = group.First(); var ids = group.Select(x => x.JobId).ToArray();
        return first with { RelatedJobIds = ids, Name = ids.Length > 1 ? first.Name + $" (+{ids.Length - 1} công việc cùng kết nối)" : first.Name };
    }).ToArray();
    public bool Update(IEnumerable<VideoJob> jobs)
    {
        var next = jobs.Select(JobAttention.From).OfType<JobAttention>().ToDictionary(x => x.JobId, StringComparer.Ordinal);
        var addedOrChanged = next.Values.Any(x => pending.TryGetValue(x.JobId, out var old) ? old.Signature != x.Signature
            : !pending.Values.Any(other => x.DependencyKey is not null && other.DependencyKey == x.DependencyKey));
        pending.Clear(); foreach (var item in next) pending.Add(item.Key, item.Value);
        return addedOrChanged;
    }
}
