using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TimVideoHay.Core;
using TimVideoHay.Infrastructure;

namespace TimVideoHay.Desktop;

public sealed class AddActionsWindow : Window
{
    private VideoJob job;
    private readonly AppSettings targets;
    private readonly GoogleSheets sheets;
    private readonly CancellationTokenSource cancel;
    private readonly Dictionary<JobActions, CheckBox> choices = new();
    private readonly StackPanel choicesPanel = new();
    private readonly CheckBox covers = new() { Content = "Tải lại ảnh bìa", Margin = new(0, 7, 0, 7) };
    private readonly Func<bool> canRefreshCovers;
    public bool RefreshCovers => covers.IsVisible && covers.IsEnabled && covers.IsChecked == true;
    private readonly TextBlock progress = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 14) };
    public Func<Task<VideoJob>>? RefreshJob { get; set; }
    public Func<CancellationToken, Task<VideoJob>>? CheckResults { get; set; }
    private bool checking;
    public Func<Task<VideoJob?>>? ChooseMovedFolder { get; set; }
    private readonly Button relocate = new() { Content = "Chọn vị trí thư mục đã chuyển", HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private bool relocating;
    private string relocationError = "";
    private readonly System.Windows.Threading.DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private bool refreshing;
    private readonly CheckBox current = new() { Content = "Dùng đích hiện tại thay đích cũ cho phần thực hiện lại", Margin = new(0, 12, 0, 8) };
    private readonly ComboBox niches = new() { IsEditable = false, Margin = new(0, 8, 0, 8) };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) };
    private readonly Button confirm = new() { Content = "Thực hiện phần đã chọn", Padding = new(12, 8, 12, 8), IsEnabled = false };
    private int loadVersion;
    private bool loading;
    private SheetTarget? loadedTarget;
    private string nicheStatus = "";
    private readonly Func<SheetTarget, CancellationToken, Task<IReadOnlyList<string>>> readNiches;
    internal JobActions VisibleActions => choices.Keys.Aggregate(JobActions.None, (a, x) => a | x);
    internal bool DownloadLocked => choices.TryGetValue(JobActions.Download, out var box) && !box.IsEnabled;
    internal bool CoversAvailable => covers.Visibility == Visibility.Visible && covers.IsEnabled;
    internal bool CanConfirm => confirm.IsEnabled;
    internal void SelectCoversForSmoke() => covers.IsChecked = true;
    internal void SelectForSmoke(JobActions action) => choices[action].IsChecked = true;
    internal void ApplyFreshSnapshot(VideoJob next) { job = next; RebuildChoices(); Update(); }
    public JobActions Actions => choices.Where(x => x.Value.IsChecked == true).Aggregate(JobActions.None, (a, x) => a | x.Key);
    public string Niche => niches.SelectedItem as string ?? job.Niche;
    public bool UseCurrentTargets => current.IsChecked == true;
    public AddActionsWindow(VideoJob job, AppSettings targets, GoogleSheets sheets, CancellationToken ct, Func<SheetTarget, CancellationToken, Task<IReadOnlyList<string>>>? nicheReader = null, Func<bool>? canRefreshCovers = null)
    {
        this.canRefreshCovers = canRefreshCovers ?? (() => true);
        this.job = job; this.targets = targets; this.sheets = sheets; cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readNiches = nicheReader ?? sheets.NichesAsync;
        Title = "Bổ sung / thực hiện lại"; Width = 650; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new(26) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Bổ sung / thực hiện lại", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = job.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        panel.Children.Add(progress); panel.Children.Add(relocate); panel.Children.Add(choicesPanel);
        var checkResults = new Button { Content = "Kiểm tra lại kết quả Google", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
        panel.Children.Add(checkResults);
        checkResults.Click += async (_, _) => {
            if (CheckResults is null || checking) return;
            checking = true; checkResults.IsEnabled = false; relocate.IsEnabled = false; UpdateConfirmation();
            try { var fresh = await CheckResults(cancel.Token); if (!cancel.IsCancellationRequested) { job = fresh; RebuildChoices(); Update(); } }
            catch (OperationCanceledException) { }
            catch (Exception ex) { details.Text = ex is NeedsActionException ? ex.Message : "Chưa kiểm tra được kết quả; trạng thái cũ được giữ."; }
            finally { checking = false; checkResults.IsEnabled = true; relocate.IsEnabled = true; UpdateConfirmation(); }
        };
        relocate.Click += async (_, _) => {
            if (ChooseMovedFolder is null) return;
            relocate.IsEnabled = false; relocating = true; relocationError = ""; UpdateConfirmation();
            try { var fresh = await ChooseMovedFolder(); if (fresh is not null) ApplyFreshSnapshot(fresh); }
            catch (Exception ex) { relocationError = ex is NeedsActionException ? ex.Message : "Chưa liên kết được thư mục. Kiểm tra đúng file và thử lại."; RebuildChoices(); }
            finally { relocate.IsEnabled = true; relocating = false; UpdateConfirmation(); }
        };
        covers.Checked += Change; covers.Unchecked += Change; RebuildChoices();
        panel.Children.Add(current); current.Checked += Change; current.Unchecked += Change;
        panel.Children.Add(details); panel.Children.Add(niches);
        niches.SelectionChanged += (_, _) => UpdateConfirmation();
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; var close = new Button { Content = "Hủy", IsCancel = true };
        confirm.Style = (Style)FindResource("Primary");
        buttons.Children.Add(close); buttons.Children.Add(confirm);
        DialogLayout.Apply(this, panel, buttons, 590);
        confirm.Click += (_, _) => { if (confirm.IsEnabled) DialogResult = true; };
        refresh.Tick += async (_, _) => {
            if (refreshing || checking || RefreshJob is null) return;
            refreshing = true;
            try { job = await RefreshJob(); RebuildChoices(); Update(); }
            catch { confirm.IsEnabled = false; details.Text = "Không đọc được trạng thái mới. Đóng và mở lại lựa chọn bổ sung."; }
            finally { refreshing = false; }
        };
        Loaded += (_, _) => { checkResults.Visibility = CheckResults is null || job.Sheet is null && job.Channel is null ? Visibility.Collapsed : Visibility.Visible; refresh.Start(); }; Closed += (_, _) => { refresh.Stop(); cancel.Cancel(); }; Update();
    }
    private void RebuildChoices()
    {
        foreach (var action in choices.Keys.ToArray())
            if (!job.Addable.HasFlag(action)) { choicesPanel.Children.Remove(choices[action]); choices.Remove(action); }
        foreach (var action in new[] { JobActions.Download, JobActions.WriteSheet, JobActions.UploadYouTube })
        {
            if (!job.Addable.HasFlag(action) || choices.ContainsKey(action)) continue;
            var box = new CheckBox { Content = action == JobActions.Download ? "Tải video và ảnh bìa" : action == JobActions.WriteSheet ? "Điền Google Sheets" : "Đăng YouTube", Margin = new(0, 7, 0, 7) };
            choices.Add(action, box); box.Checked += Change; box.Unchecked += Change; choicesPanel.Children.Add(box);
        }
        current.Visibility = job.FolderDeleted || job.SheetDeleted || job.YouTubeDeleted ? Visibility.Visible : Visibility.Collapsed;
        if (!choicesPanel.Children.Contains(covers)) choicesPanel.Children.Add(covers);
        covers.Visibility = job.CanRefreshCovers ? Visibility.Visible : Visibility.Collapsed;
        covers.IsEnabled = job.CanRefreshCovers && canRefreshCovers();
        covers.ToolTip = covers.IsEnabled ? "Cập nhật ảnh bìa; giữ video và các kết quả Google đã có." : "Chờ thao tác đang dùng kết quả của công việc này kết thúc.";
        if (!covers.IsEnabled) covers.IsChecked = false;
        progress.Text = job.ResultsLabel + (job.Work != JobActions.None ? "\nPhần đang dở sẽ tiếp tục cùng lượt này: " + VideoJob.ActionLabel(job.Work) : "");
        relocate.Visibility = job.DownloadDirectory is not null && !job.FolderDeleted && !System.IO.Directory.Exists(job.DownloadDirectory) ? Visibility.Visible : Visibility.Collapsed;
        if (relocate.Visibility == Visibility.Visible) progress.Text += "\nKhông thấy thư mục cũ. Chọn vị trí mới nếu đã chuyển; chỉ tải lại sau khi xác định file đã mất.";
        if (relocationError.Length > 0) progress.Text += "\n" + relocationError;
    }
    private void Change(object sender, RoutedEventArgs e) => Update();
    private void Update()
    {
        var uploading = choices.TryGetValue(JobActions.UploadYouTube, out var upload) && upload.IsChecked == true;
        if (choices.TryGetValue(JobActions.Download, out var download))
        {
            if (uploading && (!job.HasDownload || job.MissingDownload)) download.IsChecked = true;
            download.IsEnabled = !uploading || job.HasDownload && !job.MissingDownload;
        }
        var sheet = SheetTarget();
        details.Text = (Actions.HasFlag(JobActions.Download) ? "Thư mục: " + (job.DownloadDirectory is not null && !job.FolderDeleted || job.FolderDeleted && !UseCurrentTargets ? job.Folder : targets.CurrentFolder) + "\n" : uploading && job.HasDownload ? "Dùng file đã tải; app kiểm tra file trước khi đăng.\n" : "")
            + (uploading && job.Work.HasFlag(JobActions.Download) ? "Phần tải đang dở sẽ tiếp tục từ checkpoint.\n" : "")
            + (Actions.HasFlag(JobActions.WriteSheet) ? $"Bảng: {sheet?.SpreadsheetName} / {sheet?.TabName}\n" : "")
            + (uploading ? $"Kênh: {(job.YouTubeDeleted && !UseCurrentTargets ? job.Channel : targets.Channel)?.Name}\nHiển thị: {targets.Privacy switch { "private" => "Riêng tư", "public" => "Công khai", _ => "Không công khai" }}" : "");
        if (Actions.HasFlag(JobActions.WriteSheet)) details.Text += "\n" + nicheStatus;
        niches.Visibility = Actions.HasFlag(JobActions.WriteSheet) ? Visibility.Visible : Visibility.Collapsed;
        if (Actions.HasFlag(JobActions.WriteSheet) && sheet != loadedTarget) _ = LoadNichesAsync(sheet);
        UpdateConfirmation();
    }
    private SheetTarget? SheetTarget() => job.SheetDeleted && !UseCurrentTargets ? job.Sheet : targets.Sheet;
    private async Task LoadNichesAsync(SheetTarget? sheet)
    {
        var version = ++loadVersion; loadedTarget = sheet; loading = true; nicheStatus = "Đang tải ngách…"; niches.ItemsSource = null; UpdateConfirmation();
        try
        {
            if (sheet is null) { nicheStatus = "Cần kết nối bảng trong cài đặt."; return; }
            var options = await readNiches(sheet, cancel.Token);
            if (version != loadVersion || cancel.IsCancellationRequested) return;
            niches.ItemsSource = options; if (options.Contains(job.Niche)) niches.SelectedItem = job.Niche;
            nicheStatus = "Chọn ngách của bảng phía trên.";
        }
        catch (Exception ex) { if (version == loadVersion && !cancel.IsCancellationRequested) nicheStatus = ex is NeedsAuthException or NeedsActionException ? ex.Message : "Không tải được ngách. Kiểm tra kết nối rồi mở lại."; }
        finally { if (version == loadVersion) { loading = false; Update(); } }
    }
    private void UpdateConfirmation() => confirm.IsEnabled = !relocating && !checking && (Actions != JobActions.None || RefreshCovers) && !loading
        && (!Actions.HasFlag(JobActions.WriteSheet) || niches.SelectedItem is string)
        && (!Actions.HasFlag(JobActions.UploadYouTube) || (job.YouTubeDeleted && !UseCurrentTargets ? job.Channel : targets.Channel) is not null);
}
