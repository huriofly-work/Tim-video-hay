using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TimVideoHay.Core;

public enum JobMode { Full, DownloadOnly } // Legacy schema only.
[Flags] public enum JobActions { None = 0, Download = 1, WriteSheet = 2, UploadYouTube = 4, All = 7 }
public sealed record SourceMetadata(string SourceId, long DurationMilliseconds, string Provider, DateTimeOffset ReadAt);
public sealed record ChannelVideo(string Id, long PublishedAt, long DurationMilliseconds, bool IsPinned = false);
public sealed record SourceChannelMetadata(string SecUid, string OriginalName, string EnglishName, long Followers,
    IReadOnlyList<ChannelVideo> LatestVideos, DateTimeOffset ReadAt)
{
    public string Url => "https://www.douyin.com/user/" + SecUid;
    public string ContentType => Classify(300000);
    public string Classify(long shortMaxMilliseconds) => LatestVideos.Select(v => Rules.VideoTypeMilliseconds(v.DurationMilliseconds, shortMaxMilliseconds)).Distinct().Count() > 1 ? "All"
        : LatestVideos.Count > 0 ? Rules.VideoTypeMilliseconds(LatestVideos[0].DurationMilliseconds, shortMaxMilliseconds) : throw new NeedsActionException("Kênh chưa có video công khai để phân loại.");
}
public sealed record ActionReceipt(JobActions Action, string AttemptId, DateTimeOffset ArchivedAt, string? Directory, MediaFile? Media,
    SheetTarget? Sheet, int? Row, string? SheetType, ChannelTarget? Channel, string? YouTubeId, string? SessionKey, long UploadedBytes, bool Deleted);
public enum JobState { Queued, Running, Paused, NeedsAuth, NeedsAction, Failed, Complete, WaitingForYouTube, WaitingToRetry }
public sealed record SheetColumnMapping(int HeaderRow, int[] Columns)
{
    public bool Equals(SheetColumnMapping? other) => other is not null && HeaderRow == other.HeaderRow
        && (ReferenceEquals(Columns, other.Columns) || Columns is not null && other.Columns is not null && Columns.SequenceEqual(other.Columns));
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(HeaderRow);
        if (Columns is not null) foreach (var column in Columns) hash.Add(column);
        return hash.ToHashCode();
    }
}
public sealed record SheetColumnOption(int Index, string Letter, string Header)
{
    public string Label => Letter + " — " + (string.IsNullOrWhiteSpace(Header) ? "(không có tiêu đề)" : Header);
}
public sealed record SheetColumnSelection(int HeaderRow, IReadOnlyList<SheetColumnOption> Options, SheetColumnMapping? Suggested);
public sealed record SheetTarget(string SpreadsheetId, int SheetId, string SpreadsheetName, string TabName, string CredentialId,
    int? SourceChannelSheetId = null, string? SourceChannelTabName = null, SheetColumnMapping? Columns = null, SheetColumnMapping? SourceChannelColumns = null);
public sealed record ChannelTarget(string ChannelId, string Name, string CredentialId);
public sealed record LocalFileIdentity(string Volume, ulong Serial, ulong Low, ulong High, ulong Created);
public sealed record SourceVideo(string Id, string CanonicalUrl);
public sealed record MediaCover(string Path, int Width, int Height, string SourceField, string Sha256)
{
    public string Orientation => Width > Height ? "ngang" : Height > Width ? "dọc" : "vuông";
}
public sealed record MediaFile(string Path, double Duration, long Length, string Sha256, string MimeType,
    int Width = 0, int Height = 0, string? Codec = null, string? FormatId = null, string? CoverPath = null, string? CoverStatus = null, string? QualityStatus = null,
    IReadOnlyList<MediaCover>? Covers = null)
{
    public string QualityLabel => Width > 0 ? $"{Width}×{Height} · {Codec}" : "";
}
public sealed record SheetTab(int Id, string Name);
public sealed record SheetSelection(string Id, string Name, IReadOnlyList<SheetTab> Tabs, string CredentialId);
public sealed class AppSettings
{
    public List<OperationNotice> PendingOperations { get; set; } = [];
    public string? VideoFolder { get; set; }
    public JobActions LastActions { get; set; } = JobActions.All;
    public string CurrentFolder => VideoFolder ?? FullDownloadFolder ?? DownloadFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Tìm video hay", "Xử lý video");
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DownloadFolder { get; set; } // Read the old shared setting once.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? FullDownloadFolder { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? DownloadOnlyFolder { get; set; }
    public void NormalizeFolders()
    {
        VideoFolder ??= CurrentFolder;
        FullDownloadFolder = null; DownloadOnlyFolder = null; DownloadFolder = null;
        if ((LastActions & ~JobActions.All) != 0) LastActions = JobActions.All;
        if (LastActions.HasFlag(JobActions.UploadYouTube)) LastActions |= JobActions.Download;
    }
    public SheetTarget? Sheet { get; set; }
    public ChannelTarget? Channel { get; set; }
    public string Privacy { get; set; } = "unlisted";
    public long ShortMaxMilliseconds { get; set; } = 300000;
}
public sealed class VideoJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public JobMode Mode { get; set; }
    public JobActions? Actions { get; set; }
    public JobActions? PendingActions { get; set; }
    public JobActions SelectedActions => Actions ?? (Mode == JobMode.Full ? JobActions.All : JobActions.Download);
    public JobActions CurrentActions => (SelectedActions | Results | Work)
        & ~(FolderDeleted ? JobActions.Download : 0) & ~(SheetDeleted ? JobActions.WriteSheet : 0) & ~(YouTubeDeleted ? JobActions.UploadYouTube : 0);
    public JobActions Work => PendingActions ?? (SelectedActions & ~(HasDownload ? JobActions.Download : 0) & ~(SheetVerified && !SheetDeleted ? JobActions.WriteSheet : 0) & ~(YouTubeVerified && !YouTubeDeleted ? JobActions.UploadYouTube : 0));
    public bool HasDownload => Media is not null && !FolderDeleted;
    public bool MissingDownload => HasDownload && !File.Exists(Media!.Path);
    public bool UnknownSheet => SheetWriteStarted && !SheetVerified && !SheetDeleted;
    public bool UnknownUpload => MediaSendStarted && YouTubeId is null && !YouTubeDeleted;
    public bool UnknownResult => UnknownSheet || UnknownUpload;
    public JobActions Results => (DownloadDirectory is not null && !FolderDeleted ? JobActions.Download : 0)
        | (SheetVerified && !SheetDeleted ? JobActions.WriteSheet : 0) | (YouTubeId is not null && !YouTubeDeleted ? JobActions.UploadYouTube : 0);
    public JobActions Addable => RemovedFromHistory || DeletionRequested || UnknownResult || State is JobState.Running or JobState.Queued or JobState.WaitingForYouTube or JobState.WaitingToRetry ? JobActions.None
        : (JobActions.All & ~SelectedActions) | (FolderDeleted || MissingDownload ? JobActions.Download : 0) | (SheetDeleted ? JobActions.WriteSheet : 0) | (YouTubeDeleted ? JobActions.UploadYouTube : 0);
    public bool CanAdd => Addable != JobActions.None;
    public bool CanRefreshCovers => !RemovedFromHistory && !DeletionRequested && !UnknownResult
        && State is not (JobState.Running or JobState.Queued or JobState.WaitingForYouTube or JobState.WaitingToRetry)
        && HasDownload && !MissingDownload && DownloadDirectory is not null && Directory.Exists(DownloadDirectory);
    public bool CanSupplement => CanAdd || CanRefreshCovers || (!RemovedFromHistory && !DeletionRequested && !UnknownResult
        && State is not (JobState.Running or JobState.Queued or JobState.WaitingForYouTube or JobState.WaitingToRetry) && Results != JobActions.None);
    public bool CanPause => !RemovedFromHistory && State is JobState.Running or JobState.Queued or JobState.WaitingForYouTube or JobState.WaitingToRetry;
    public bool CanResume => !RemovedFromHistory && State is JobState.Paused or JobState.NeedsAuth or JobState.NeedsAction or JobState.Failed;
    public bool CanTogglePause => CanPause || CanResume;
    public string PauseResumeLabel => CanPause ? "Tạm dừng" : "Tiếp tục";
    public bool NeedsGoogleReconnect => State == JobState.NeedsAuth && ((Work & (JobActions.WriteSheet | JobActions.UploadYouTube)) != 0 || DeletionRequested && (DeleteSheetRequested || DeleteYouTubeRequested));
    public bool NeedsReconciliation => State == JobState.NeedsAction && (UnknownResult || SheetWriteStarted || MediaSendStarted);
    public string AttemptId { get; set; } = Guid.NewGuid().ToString("N");
    public string? DownloadAttemptId { get; set; }
    public string? SheetAttemptId { get; set; }
    public string? YouTubeAttemptId { get; set; }
    public List<ActionReceipt> Receipts { get; set; } = [];
    public SourceMetadata? Metadata { get; set; }
    public SourceChannelMetadata? SourceChannel { get; set; }
    public bool IncludeSourceChannel { get; set; } = true;
    public SheetTarget? SourceChannelSheet { get; set; }
    public bool SourceChannelUpdated { get; set; }
    public string SourceChannelMessage => SourceChannelSheet is null || !SheetVerified ? "" : SheetDeleted ? "Dòng Kênh nguồn được giữ khi xóa dòng video."
        : SourceChannelUpdated ? "Đã cập nhật kênh đã có trong Kênh nguồn." : "Đã thêm kênh mới vào Kênh nguồn.";
    public bool YouTubeVerified { get; set; }
    public bool RemoveHistoryRequested { get; set; }
    public JobState State { get; set; } = JobState.Queued;
    public JobIssue? Issue { get; set; }
    public JobActions StageAction { get; set; }
    public JobActions OmittedActions { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public int AutomaticRetryAttempts { get; set; }
    public List<JobDecisionReceipt> Decisions { get; set; } = [];
    public bool RemovedFromHistory { get; set; }
    public bool DeletionRequested { get; set; }
    public bool DeleteSheetRequested { get; set; }
    public bool DeleteYouTubeRequested { get; set; }
    public bool DeleteFolderRequested { get; set; }
    public bool FolderDeleteStarted { get; set; }
    public bool FolderDeleted { get; set; }
    public bool FolderAbsenceConfirmed { get; set; }
    public bool SheetDeleteStarted { get; set; }
    public bool SheetDeleted { get; set; }
    public bool YouTubeDeleteStarted { get; set; }
    public bool YouTubeDeleted { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EnqueuedAt { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public int QueuePosition { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string QueueLabel => State == JobState.Queued && QueuePosition > 0 ? QueuePosition.ToString() : "";
    public string Input { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string VideoLink => string.IsNullOrWhiteSpace(Source?.CanonicalUrl) ? Input : Source.CanonicalUrl;
    public string Name { get; set; } = "";
    public string Niche { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Privacy { get; set; } = "private";
    public long ShortMaxMilliseconds { get; set; } = 300000;
    public string Folder { get; set; } = "";
    public LocalFileIdentity? DirectoryIdentity { get; set; }
    public LocalFileIdentity? VideoIdentity { get; set; }
    public SheetTarget? Sheet { get; set; }
    public ChannelTarget? Channel { get; set; }
    public SourceVideo? Source { get; set; }
    public MediaFile? Media { get; set; }
    public string? DownloadDirectory { get; set; }
    public string? DownloadFormatId { get; set; }
    public string? DownloadExtension { get; set; }
    public bool SheetWriteStarted { get; set; }
    public bool SheetVerified { get; set; }
    public int? SheetRow { get; set; }
    public string? SheetTypeValue { get; set; }
    // Secret URL lives in the DPAPI store, never in this JSON or logs.
    public string? UploadSessionKey { get; set; }
    public long UploadedBytes { get; set; }
    public bool MediaSendStarted { get; set; }
    public string? YouTubeId { get; set; }
    public DateTimeOffset? NextYouTubeCheckAt { get; set; }
    public int YouTubeCheckAttempts { get; set; }
    public DateTimeOffset? YouTubeVerificationStartedAt { get; set; }
    public string Stage { get; set; } = "Chờ xử lý";
    public string Message { get; set; } = "";
    public double Progress { get; set; }
    public JobActions ProgressAction { get; set; }
    public long DownloadedBytes { get; set; }
    public long DownloadTotalBytes { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public double DisplayProgress => State == JobState.Complete ? 100
        : ProgressAction == JobActions.Download && DownloadTotalBytes > 0 ? Math.Clamp(100d * DownloadedBytes / DownloadTotalBytes, 0, 100)
        : ProgressAction == JobActions.UploadYouTube && Media is { Length: > 0 } ? Math.Clamp(100d * UploadedBytes / Media.Length, 0, 100)
        : double.IsFinite(Progress) ? Math.Clamp(Progress, 0, 100) : 0;
    [System.Text.Json.Serialization.JsonIgnore] public string ProgressLabel => State == JobState.Complete ? "Hoàn tất" : ProgressAction switch
    { JobActions.Download => "Tải video", JobActions.UploadYouTube => "Đăng YouTube", _ => "Tiến độ đã lưu" };
    public void RestoreUploadProgress()
    {
        if (YouTubeId is not null && !YouTubeDeleted)
        { if (Media is { Length: > 0 }) UploadedBytes = Media.Length; ProgressAction = JobActions.UploadYouTube; Progress = 100; return; }
        if (!YouTubeDeleted && Media is { Length: > 0 } && (UploadSessionKey is not null || YouTubeId is not null || MediaSendStarted))
        { ProgressAction = JobActions.UploadYouTube; Progress = Math.Clamp(100d * UploadedBytes / Media.Length, 0, 100); }
    }
    public string Type => Metadata is not null ? Rules.VideoTypeMilliseconds(Metadata.DurationMilliseconds, ShortMaxMilliseconds) : SheetTypeValue ?? "";
    public string StateLabel => State switch { JobState.Queued => "Đang chờ", JobState.Running => "Đang chạy", JobState.WaitingForYouTube => "Chờ xác nhận", JobState.WaitingToRetry => "Chờ tự thử lại", JobState.Paused => "Tạm dừng", JobState.NeedsAuth => "Cần đăng nhập", JobState.NeedsAction => "Chờ lựa chọn", JobState.Failed => "Chờ xử lý", _ => "Hoàn tất" };
    public string ModeLabel => ActionLabel(CurrentActions);
    public string WorkDetail => DeletionRequested ? "Đang xóa: " + ActionLabel((DeleteFolderRequested && !FolderDeleted ? JobActions.Download : 0)
        | (DeleteSheetRequested && !SheetDeleted ? JobActions.WriteSheet : 0) | (DeleteYouTubeRequested && !YouTubeDeleted ? JobActions.UploadYouTube : 0)) : ResultsLabel;
    public string ResultsLabel => $"Tải: {(FolderDeleted ? "đã xóa" : MissingDownload ? "thiếu file/đã chuyển" : HasDownload ? "đã có" : Work.HasFlag(JobActions.Download) ? "chưa xong" : "chưa chọn")} · Bảng: {(SheetDeleted ? "đã xóa" : SheetVerified ? "đã có" : UnknownSheet ? "cần xác minh" : Work.HasFlag(JobActions.WriteSheet) ? "chưa xong" : "chưa chọn")} · YouTube: {(YouTubeDeleted ? "đã xóa" : YouTubeVerified ? "đã có" : YouTubeId is not null || UnknownUpload ? "cần xác minh" : Work.HasFlag(JobActions.UploadYouTube) ? "chưa xong" : "chưa chọn")}";
    public static string ActionLabel(JobActions actions) => string.Join(" + ", new[] { actions.HasFlag(JobActions.Download) ? "Tải" : null, actions.HasFlag(JobActions.WriteSheet) ? "Bảng" : null, actions.HasFlag(JobActions.UploadYouTube) ? "YouTube" : null }.Where(x => x is not null));
    public void MigrateWorkflow()
    {
        if (Actions is not null) return;
        Actions = SelectedActions;
        YouTubeVerified = State == JobState.Complete && YouTubeId is not null && !YouTubeDeleted;
        RemoveHistoryRequested = DeletionRequested;
        if (Source is null && Uri.TryCreate(Input, UriKind.Absolute, out var uri) && Rules.SourceId(uri) is string id) Source = new(id, "https://www.douyin.com/video/" + id);
        PendingActions = State == JobState.Complete || RemovedFromHistory ? JobActions.None : Work;
    }
}
public static partial class Rules
{
    public const int MaxVideoNameLength = 30;
    public static int NameLength(string value) => StringInfo.ParseCombiningCharacters(value).Length;
    public static string LimitNameInput(string value)
    {
        var starts = StringInfo.ParseCombiningCharacters(value);
        return starts.Length > MaxVideoNameLength ? value[..starts[MaxVideoNameLength]] : value;
    }
    public static string VideoName(string value)
    {
        if (NameLength(value) > MaxVideoNameLength)
            throw new ArgumentException("Tên video tối đa 30 ký tự, bao gồm dấu cách.");
        return Text(value, "Tên video", 100);
    }
    public static readonly string[] Headers = ["Link video mẫu", "Tên video", "Ngách", "Loại video"];
    public static readonly string[] ChannelHeaders = ["Link kênh", "Tên kênh", "Đăng ký", "Ngách", "Dạng nội dung"];
    public static bool SameTitle(string left, string right) => string.Equals(left.Normalize().Trim(), right.Normalize().Trim(), StringComparison.OrdinalIgnoreCase);
    public static string VideoType(double seconds) => double.IsFinite(seconds) && seconds > 0
        ? seconds <= 300 ? "Short" : "Long" : throw new ArgumentException("Không xác định được thời lượng video.");
    public static string VideoTypeMilliseconds(long ms, long shortMaxMilliseconds = 300000) => ms > 0 && shortMaxMilliseconds is >= 1000 and <= 86400000
        ? ms <= shortMaxMilliseconds ? "Short" : "Long" : throw new ArgumentException("Thời lượng hoặc mốc phân loại không hợp lệ.");
    public static string Text(string value, string label, int max)
    {
        value = value.Normalize(NormalizationForm.FormC).Trim();
        if (value.Length == 0 || value.EnumerateRunes().Count() > max || value.Any(char.IsControl))
            throw new ArgumentException($"{label} cần từ 1 đến {max} ký tự, không chứa ký tự điều khiển.");
        return value;
    }
    public static void Validate(VideoJob job)
    {
        _ = VideoTypeMilliseconds(1, job.ShortMaxMilliseconds);
        job.Name = VideoName(job.Name);
        _ = DouyinUri(job.Input);
        var actions = job.SelectedActions;
        if (actions == JobActions.None || (actions & ~JobActions.All) != 0 || actions.HasFlag(JobActions.UploadYouTube) && !actions.HasFlag(JobActions.Download)) throw new ArgumentException("Chọn ít nhất một công việc; đăng YouTube cần tải video.");
        if (actions.HasFlag(JobActions.Download) && !Path.IsPathFullyQualified(job.Folder)) throw new ArgumentException("Chọn thư mục tải hợp lệ.");
        if (actions.HasFlag(JobActions.WriteSheet)) { job.Niche = Text(job.Niche, "Ngách", 200); if (job.Sheet is null) throw new ArgumentException("Chọn bảng và tab trước."); }
        if (!actions.HasFlag(JobActions.UploadYouTube)) return;
        job.Title = Text(string.IsNullOrWhiteSpace(job.Title) ? job.Name : job.Title, "Tiêu đề YouTube", 100);
        if (job.Title.Contains('<') || job.Title.Contains('>')) throw new ArgumentException("Tiêu đề YouTube không được chứa < hoặc >.");
        if (Encoding.UTF8.GetByteCount(job.Description) > 5000 || job.Description.Contains('<') || job.Description.Contains('>'))
            throw new ArgumentException("Mô tả tối đa 5.000 byte UTF-8 và không chứa < hoặc >.");
        if (job.Channel is null) throw new ArgumentException("Kết nối kênh YouTube trước.");
        if (job.Privacy is not ("private" or "unlisted" or "public")) throw new ArgumentException("Chế độ hiển thị không hợp lệ.");
    }
    public static Uri DouyinUri(string input)
    {
        var match = LinkRegex().Match(input.Trim());
        if (!Uri.TryCreate(match.Success ? match.Value : input.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("Nhập link video Douyin.");
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Host is not ("douyin.com" or "www.douyin.com" or "v.douyin.com" or "iesdouyin.com" or "www.iesdouyin.com"))
            throw new ArgumentException("Hiện chỉ hỗ trợ link HTTPS Douyin.");
        return uri;
    }
    public static string? SourceId(Uri uri)
    {
        var m = IdRegex().Match(uri.AbsolutePath);
        return m.Success ? m.Groups[1].Value : null;
    }
    public static string FileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        result = string.Concat(result.EnumerateRunes().Take(100).Select(r => r.ToString()));
        if (result.Length == 0) result = "video";
        if (Regex.IsMatch(result, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) result = "_" + result;
        return result;
    }
    [GeneratedRegex(@"https://[^\s<>]+", RegexOptions.IgnoreCase)] private static partial Regex LinkRegex();
    [GeneratedRegex(@"^/(?:video|share/video)/(\d{15,22})/?$")] private static partial Regex IdRegex();
}
public sealed class NeedsAuthException(string message) : Exception(message);
public class NeedsActionException(string message, JobIssueKind kind = JobIssueKind.General) : Exception(message)
{
    public JobIssueKind Kind { get; } = kind;
}
public sealed class YouTubePendingException(string message) : Exception(message);


public sealed class DuplicateJobException(VideoJob existing) : Exception("Video đã có công việc trong lịch sử.") { public VideoJob Existing { get; } = existing; }
