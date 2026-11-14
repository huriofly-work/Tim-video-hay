using System.Windows;
using TimVideoHay.Core;

namespace TimVideoHay.Desktop;

public partial class DeleteJobWindow : Window
{
    private VideoJob job;
    public System.Func<System.Threading.Tasks.Task<VideoJob>>? RefreshJob { get; set; }
    public System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<VideoJob>>? CheckResults { get; set; }
    private readonly System.Threading.CancellationTokenSource checkCancellation = new();
    private bool checking;
    private System.Threading.Tasks.TaskCompletionSource? checkingFinished;
    public System.Threading.Tasks.Task WaitForChecksAsync() => checkingFinished?.Task ?? System.Threading.Tasks.Task.CompletedTask;
    public System.Func<System.Threading.Tasks.Task<VideoJob?>>? ChooseMovedFolder { get; set; }
    public bool ConfirmMissingFolder => DeleteFolder && job.DownloadDirectory is not null && !System.IO.Directory.Exists(job.DownloadDirectory);
    private readonly System.Windows.Threading.DispatcherTimer refresh = new() { Interval = System.TimeSpan.FromMilliseconds(650) };
    private bool refreshing;
    private bool? lastFolderMissing;
    private bool relocating;
    private string relocationError = "";
    public bool RemoveHistory => RemoveHistoryBox.Visibility == Visibility.Visible && RemoveHistoryBox.IsChecked == true;
    public bool DeleteSheet => DeleteSheetBox.Visibility == Visibility.Visible && DeleteSheetBox.IsChecked == true;
    public bool DeleteYouTube => DeleteVideoBox.Visibility == Visibility.Visible && DeleteVideoBox.IsChecked == true;
    public bool DeleteFolder => DeleteFolderBox.Visibility == Visibility.Visible && DeleteFolderBox.IsChecked == true;
    internal bool HasFolderChoice => DeleteFolderBox.Visibility == Visibility.Visible;
    internal bool HasSheetChoice => DeleteSheetBox.Visibility == Visibility.Visible;
    internal bool HasVideoChoice => DeleteVideoBox.Visibility == Visibility.Visible;
    internal bool HasUnknownUpload => UnknownUpload.Visibility == Visibility.Visible;
    public DeleteJobWindow(VideoJob job)
    {
        this.job = job; InitializeComponent(); JobName.Text = job.Name;
        MaxHeight = System.Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32); Height = System.Math.Min(Height, MaxHeight);
        DeleteFolderBox.Visibility = job.DownloadDirectory is not null && !job.FolderDeleted ? Visibility.Visible : Visibility.Collapsed;
        FolderPath.Text = job.DownloadDirectory;
        FolderPath.Visibility = DeleteFolderBox.Visibility;
        DeleteFolderBox.IsChecked = job.DeleteFolderRequested && !job.FolderDeleted && !job.FolderAbsenceConfirmed;
        DeleteSheetBox.Visibility = job.SheetVerified && !job.SheetDeleted ? Visibility.Visible : Visibility.Collapsed;
        KeepChannelNote.Visibility = job.SourceChannelSheet is not null && DeleteSheetBox.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        DeleteVideoBox.Visibility = job.YouTubeId is not null && !job.YouTubeDeleted ? Visibility.Visible : Visibility.Collapsed;
        if (job.UnknownResult) { UnknownUpload.Visibility = Visibility.Visible; UnknownUpload.Text = "Có kết quả chưa rõ (ghi bảng/upload). Dùng Tiếp tục hoặc Đối soát để xác minh trước; chưa thể xóa công việc khỏi lịch sử."; }
        DeleteSheetBox.IsChecked = job.DeleteSheetRequested && !job.SheetDeleted;
        DeleteVideoBox.IsChecked = job.DeleteYouTubeRequested && !job.YouTubeDeleted;
        foreach (var box in new[] { DeleteFolderBox, DeleteSheetBox, DeleteVideoBox }) { box.Checked += (_, _) => Update(); box.Unchecked += (_, _) => Update(); }
        RemoveHistoryBox.Checked += (_, _) => { if (HasFolderChoice) DeleteFolderBox.IsChecked = true; if (HasSheetChoice) DeleteSheetBox.IsChecked = true; if (HasVideoChoice) DeleteVideoBox.IsChecked = true; Update(); };
        RemoveHistoryBox.Unchecked += (_, _) => Update();
        refresh.Tick += async (_, _) => {
            if (refreshing || checking || RefreshJob is null) return;
            refreshing = true;
            try { ApplyFreshSnapshot(await RefreshJob()); } catch { ConfirmButton.IsEnabled = false; UnknownUpload.Visibility = Visibility.Visible; UnknownUpload.Text = "Không đọc được trạng thái mới. Đóng và mở lại lựa chọn xóa."; }
            finally { refreshing = false; }
        };
        Loaded += (_, _) => { CheckResultsButton.Visibility = CheckResults is null || job.Sheet is null && job.Channel is null ? Visibility.Collapsed : Visibility.Visible; refresh.Start(); };
        Closed += (_, _) => { refresh.Stop(); checkCancellation.Cancel(); };
        ApplyFreshSnapshot(job);
        Update();
    }
    internal void ApplyFreshSnapshot(VideoJob next)
    {
        var missingFolder = next.DownloadDirectory is not null && !System.IO.Directory.Exists(next.DownloadDirectory) && !next.FolderDeleted;
        if (lastFolderMissing is not null && lastFolderMissing != missingFolder) DeleteFolderBox.IsChecked = false;
        lastFolderMissing = missingFolder;
        if (job.DownloadDirectory != next.DownloadDirectory || job.DownloadAttemptId != next.DownloadAttemptId) DeleteFolderBox.IsChecked = false;
        if (!SameSheet(job.Sheet, next.Sheet) || job.SheetAttemptId != next.SheetAttemptId || job.Source?.Id != next.Source?.Id) DeleteSheetBox.IsChecked = false;
        if (job.Channel != next.Channel || job.YouTubeId != next.YouTubeId || job.YouTubeAttemptId != next.YouTubeAttemptId) DeleteVideoBox.IsChecked = false;
        job = next;
        DeleteFolderBox.Visibility = next.Results.HasFlag(JobActions.Download) ? Visibility.Visible : Visibility.Collapsed;
        DeleteSheetBox.Visibility = next.Results.HasFlag(JobActions.WriteSheet) ? Visibility.Visible : Visibility.Collapsed;
        DeleteVideoBox.Visibility = next.Results.HasFlag(JobActions.UploadYouTube) ? Visibility.Visible : Visibility.Collapsed;
        DeleteFolderBox.Content = missingFolder ? "Xác nhận thư mục đã xóa thủ công (chỉ cập nhật kết quả trong app)" : next.Media is null ? "Xóa thư mục tải dở và các tệp bên trong" : "Xóa thư mục video và các tệp bên trong";
        RelocateFolderButton.Visibility = missingFolder ? Visibility.Visible : Visibility.Collapsed;
        MissingLocalNote.Visibility = missingFolder || next.MissingDownload ? Visibility.Visible : Visibility.Collapsed;
        MissingLocalNote.Text = (missingFolder ? "Không thấy thư mục ở đường dẫn cũ. Nếu đã chuyển, chọn thư mục mới trước; app đối chiếu đúng video rồi mới liên kết. Chỉ tích xác nhận đã xóa nếu thư mục thực sự đã xóa thủ công.\n" : next.MissingDownload ? "File video đã mất; các file còn lại trong thư mục vẫn có thể xóa hoặc giữ để tải bổ sung.\n" : "");
        if (relocationError.Length > 0) { MissingLocalNote.Visibility = Visibility.Visible; MissingLocalNote.Text = relocationError + "\n" + MissingLocalNote.Text; }
        FolderPath.Text = next.DownloadDirectory; FolderPath.Visibility = DeleteFolderBox.Visibility;
        DeleteSheetBox.Content = $"Xóa dòng Sheets: {next.Sheet?.SpreadsheetName} / {next.Sheet?.TabName}";
        KeepChannelNote.Visibility = next.SourceChannelSheet is not null && DeleteSheetBox.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        DeleteVideoBox.Content = $"Xóa video YouTube trên kênh: {next.Channel?.Name}";
        UnknownUpload.Visibility = next.UnknownResult ? Visibility.Visible : Visibility.Collapsed;
        if (next.UnknownResult) UnknownUpload.Text = "Có kết quả chưa rõ (ghi bảng/upload). Cần Tiếp tục hoặc Đối soát trước khi xóa lịch sử.";
        if (!HasFolderChoice) DeleteFolderBox.IsChecked = false;
        if (!HasSheetChoice) DeleteSheetBox.IsChecked = false;
        if (!HasVideoChoice) DeleteVideoBox.IsChecked = false;
        Update();
    }
    private void Update()
    {
        var all = (!HasFolderChoice || DeleteFolder) && (!HasSheetChoice || DeleteSheet) && (!HasVideoChoice || DeleteYouTube) && !job.UnknownResult;
        if (!all) RemoveHistoryBox.IsChecked = false;
        RemoveHistoryBox.Visibility = all ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.IsEnabled = !relocating && !checking && (DeleteFolder || DeleteSheet || DeleteYouTube || RemoveHistory);
    }
    private async void CheckResultsNow(object sender, RoutedEventArgs e)
    {
        if (CheckResults is null || checking) return;
        checking = true; checkingFinished = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        CheckResultsButton.IsEnabled = false; RelocateFolderButton.IsEnabled = false; Update();
        CheckResultsStatus.Text = "Đang kiểm tra kết quả Google…";
        try { var next = await CheckResults(checkCancellation.Token); if (!checkCancellation.IsCancellationRequested) { ApplyFreshSnapshot(next); CheckResultsStatus.Text = next.Message; } }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex) { CheckResultsStatus.Text = ex is NeedsActionException ? ex.Message : "Chưa kiểm tra được kết quả; trạng thái cũ được giữ."; }
        finally { checking = false; CheckResultsButton.IsEnabled = true; RelocateFolderButton.IsEnabled = true; Update(); checkingFinished.TrySetResult(); }
    }
    private void Confirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private static bool SameSheet(SheetTarget? a, SheetTarget? b) => a is null ? b is null : b is not null
        && a.SpreadsheetId == b.SpreadsheetId && a.SheetId == b.SheetId && a.CredentialId == b.CredentialId
        && (a.Columns is null ? b.Columns is null : b.Columns is not null && a.Columns.HeaderRow == b.Columns.HeaderRow && System.Linq.Enumerable.SequenceEqual(a.Columns.Columns, b.Columns.Columns));
    private async void SelectMovedFolder(object sender, RoutedEventArgs e)
    {
        if (ChooseMovedFolder is null) return;
        RelocateFolderButton.IsEnabled = false; relocating = true; relocationError = ""; Update();
        try { var fresh = await ChooseMovedFolder(); if (fresh is not null) ApplyFreshSnapshot(fresh); }
        catch (System.Exception ex) { relocationError = ex is NeedsActionException ? ex.Message : "Chưa liên kết được thư mục. Kiểm tra đúng vị trí và file rồi thử lại."; ApplyFreshSnapshot(job); }
        finally { RelocateFolderButton.IsEnabled = true; relocating = false; Update(); }
    }
}
