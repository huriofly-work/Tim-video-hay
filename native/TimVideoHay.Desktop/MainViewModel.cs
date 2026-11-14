using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using TimVideoHay.Core;
using TimVideoHay.Infrastructure;

namespace TimVideoHay.Desktop;

public sealed record PrivacyOption(string Value, string Label);
public sealed class HistoryFilterChoice<T>(T value, string label) : INotifyPropertyChanged
{
    public T Value { get; } = value;
    public string Label { get; } = label;
    private bool selected;
    public bool IsSelected { get => selected; set { if (selected == value) return; selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly LocalStore store;
    private readonly ISecretStore secrets;
    private readonly GoogleAuth auth;
    private readonly GoogleSheets sheets;
    private readonly YouTubeDestination youtube;
    private readonly string dataRoot;
    public JobCoordinator Coordinator { get; }
    private readonly Dictionary<string, object?> values = new();
    private AppSettings settings = new();
    private JobActions lastValidActions = JobActions.All;
    private bool loading = true;
    private bool dirty;
    private readonly Dictionary<string, VideoJob> live = new();
    private readonly Dictionary<string, JobState> announcedStates = new();
    private readonly Dictionary<string, string> historySnapshots = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly DispatcherTimer refresh;
    private readonly AsyncLocal<CancellationTokenSource?> operation = new();
    private readonly HashSet<CancellationTokenSource> operations = new();
    private readonly HashSet<string> commandGroups = new(StringComparer.Ordinal);
    private bool stoppingCommands;
    private CancellationTokenSource? nicheLoad;
    private int nicheLoadVersion;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<VideoJob> Jobs { get; } = new();
    public ObservableCollection<SheetTab> SheetTabs { get; } = new();
    public ObservableCollection<string> Niches { get; } = new();
    public bool NewJobExpanded { get => Get(true); set => Set(value); }
    public bool DetailsExpanded { get => Get(false); set => Set(value); }
    public bool NicheLoading { get => Get(false); private set => Set(value); }
    public string NicheStatus { get => Get("Chọn bảng để tải danh sách ngách."); private set => Set(value); }
    public string FullNameCount => $"{Rules.NameLength(FullName)}/30 ký tự (tính cả dấu cách)";
    public PrivacyOption[] PrivacyOptions { get; } = [new("private", "Riêng tư — dùng khi thử nghiệm"), new("unlisted", "Không công khai"), new("public", "Công khai")];
    public string FullLink { get => Get(""); set { Set(value); InputChanged(); } }
    public string FullName { get => Get(""); set { if (Set(value)) { Notify(nameof(FullNameCount)); InputChanged(); } } }
    public string FullNiche { get => Get(""); set { Set(value ?? ""); InputChanged(); } }
    public string UploadTitle { get => Get(""); set { Set(value); InputChanged(); } }
    public string UploadDescription { get => Get(""); set { Set(value); InputChanged(); } }
    public string Notice { get => Get("Sẵn sàng."); private set => Set(value); }
    public void ReportSourceActivity(string message) => Notice = message;
    public string OAuthLabel { get => Get("Chưa nhập OAuth client"); private set => Set(value); }
    public bool GoogleConfigured { get => Get(false); private set => Set(value); }
    public string GoogleSetupMessage => GoogleConfigured ? "Chọn bảng, tab và kênh trong trình hướng dẫn. App lưu kết nối để dùng cho các lần sau." : "Bản cài chưa có cấu hình kết nối Google. Cần chuẩn bị cấu hình ứng dụng một lần trong Thiết lập Google nâng cao. Chọn Tải video để dùng ngay.";
    public bool Busy { get => Get(false); private set => Set(value); }
    public int TabIndex { get => Get(0); set { if (Set(value)) Notify(nameof(SelectedJob)); } }
    public string Privacy { get => settings.Privacy; set { settings.Privacy = value; Notify(); Notify(nameof(PrivacyLabel)); PersistSettings(); } }
    public string PrivacyLabel => "Chế độ đăng: " + PrivacyOptions.Single(p => p.Value == settings.Privacy).Label + ". Luôn không dành cho trẻ em.";
    public string SheetLabel => settings.Sheet is null ? "Bảng: chưa chọn" : $"Bảng: {settings.Sheet.SpreadsheetName} · Video: {settings.Sheet.TabName} · Kênh nguồn: {settings.Sheet.SourceChannelTabName ?? "Kênh nguồn"}";
    public string SheetTabsStatus { get => Get(""); private set => Set(value); }
    public string ShortMinutes
    {
        get => Get((settings.ShortMaxMilliseconds / 60000m).ToString(System.Globalization.CultureInfo.InvariantCulture));
        set
        {
            Set(value); Notify(nameof(ThresholdStatus)); InputChanged();
            if (TryThreshold(out var milliseconds)) { settings.ShortMaxMilliseconds = milliseconds; PersistSettings(); }
        }
    }
    private bool TryThreshold(out long milliseconds)
    {
        milliseconds = 0;
        if (!decimal.TryParse(ShortMinutes.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var minutes) || minutes < 1m / 60 || minutes > 1440) return false;
        milliseconds = (long)decimal.Round(minutes * 60000, 0, MidpointRounding.AwayFromZero); return true;
    }
    public string ThresholdStatus => TryThreshold(out _) ? $"≤ {ShortMinutes} phút: Short; lớn hơn: Long. Áp dụng cả video và 10 video của kênh; công việc cũ giữ mốc đã lưu." : "Nhập mốc từ 1 giây đến 1440 phút, ví dụ 5 hoặc 2,5 phút.";
    public string ColumnMappingLabel => settings.Sheet is null ? "Chọn bảng và hai tab trước." : "Video: " + MappingLabel(settings.Sheet.Columns) + " · Kênh nguồn: " + MappingLabel(settings.Sheet.SourceChannelColumns);
    private static string MappingLabel(SheetColumnMapping? mapping) => mapping is null ? "chưa chọn; tự nhận diện tên cột cũ" : "hàng " + (mapping.HeaderRow + 1) + " / " + string.Join(", ", mapping.Columns.Select(ColumnLetter));
    private static string ColumnLetter(int index) { var name = ""; for (var x = index + 1; x > 0; x = (x - 1) / 26) name = (char)('A' + (x - 1) % 26) + name; return name; }
    public SheetTab? SelectedSourceChannelTab
    {
        get => Get<SheetTab?>(null);
        set
        {
            if (!Set(value) || value is null || settings.Sheet is null || loading) return;
            if (value.Id == settings.Sheet.SheetId) { SourceTabError = "Chọn tab khác tab video; app giữ lựa chọn cũ."; loading = true; SelectedSourceChannelTab = SheetTabs.FirstOrDefault(t => t.Id == settings.Sheet.SourceChannelSheetId); loading = false; return; }
            SourceTabError = ""; VideoTabError = "";
            settings.Sheet = settings.Sheet with { SourceChannelSheetId = value.Id, SourceChannelTabName = value.Name, SourceChannelColumns = null };
            Notify(nameof(ColumnMappingLabel));
            Notify(nameof(SheetLabel)); PersistSettings();
        }
    }
    public string ChannelLabel => settings.Channel is null ? "YouTube: chưa kết nối" : $"YouTube: {settings.Channel.Name}  ({settings.Channel.ChannelId})";
    public string FolderLabel => settings.CurrentFolder;
    public VideoJob? SelectedJob { get => Get<VideoJob?>(null); set => Set(value); }
    public bool DownloadSelected { get => settings.LastActions.HasFlag(JobActions.Download); set { if (!value && UploadSelected) return; SelectAction(JobActions.Download, value); } }
    public bool SheetSelected { get => settings.LastActions.HasFlag(JobActions.WriteSheet); set { SelectAction(JobActions.WriteSheet, value); _ = LoadNichesAsync(); } }
    public bool UploadSelected { get => settings.LastActions.HasFlag(JobActions.UploadYouTube); set { SelectAction(JobActions.UploadYouTube, value); if (value) SelectAction(JobActions.Download, true); Notify(nameof(DownloadEditable)); } }
    public bool DownloadEditable => !UploadSelected;
    public string HistorySearch { get => Get(""); set { Set(value); FiltersChanged(); } }
    public string HistoryFilterLabel => StateChoices.Any(c => c.IsSelected) || ActionChoices.Any(c => c.IsSelected) || !string.IsNullOrWhiteSpace(HistorySearch)
        ? "Tìm kiếm và bộ lọc (đang áp dụng)" : "Tìm kiếm và bộ lọc";
    public string StateFilterSummary => ChoiceSummary(StateChoices, "Tất cả trạng thái");
    public string ActionFilterSummary => ChoiceSummary(ActionChoices, "Tất cả công việc");
    private static string ChoiceSummary<T>(HistoryFilterChoice<T>[] choices, string all) => choices.Any(c => c.IsSelected)
        ? string.Join(", ", choices.Where(c => c.IsSelected).Select(c => c.Label)) : all;
    private void FiltersChanged() { Notify(nameof(HistoryFilterLabel)); Notify(nameof(StateFilterSummary)); Notify(nameof(ActionFilterSummary)); dirty = true; }
    public string SortFilter { get => Get("Theo tiến trình"); set { Set(value); dirty = true; } }
    public string HistorySummary { get => Get("Chưa có công việc."); private set => Set(value); }
    public HistoryFilterChoice<JobActions>[] ActionChoices { get; } = [new(JobActions.Download, "Tải video"), new(JobActions.WriteSheet, "Điền bảng"), new(JobActions.UploadYouTube, "Đăng YouTube"), new(JobActions.None, "Trống")];
    public HistoryFilterChoice<HistoryState>[] StateChoices { get; } = [new(HistoryState.Running, "Đang chạy"), new(HistoryState.Queued, "Hàng chờ"), new(HistoryState.Waiting, "Chờ tự xử lý / xác nhận"), new(HistoryState.Attention, "Cần xử lý"), new(HistoryState.Paused, "Tạm dừng"), new(HistoryState.Complete, "Hoàn tất"), new(HistoryState.Unfinished, "Chưa hoàn tất")];
    public string[] SortFilters { get; } = ["Theo tiến trình", "Ngày tạo: mới nhất", "Ngày tạo: cũ nhất"];
    public ICommand ResetHistoryFiltersCommand { get; }
    public ICommand RefreshHistoryCommand { get; }
    public ICommand ClearStateFilterCommand { get; }
    public ICommand ClearActionFilterCommand { get; }
    public ICommand ClearHistorySearchCommand { get; }
    private void SelectAction(JobActions action, bool enabled)
    {
        settings.LastActions = enabled ? settings.LastActions | action : settings.LastActions & ~action;
        if (settings.LastActions != JobActions.None) lastValidActions = settings.LastActions.HasFlag(JobActions.UploadYouTube) ? settings.LastActions | JobActions.Download : settings.LastActions;
        InputChanged();
        foreach (var prop in new[] { nameof(DownloadSelected), nameof(SheetSelected), nameof(UploadSelected) }) Notify(prop);
        PersistSettings();
    }
    public ICommand AddActionsCommand { get; }
    public SheetTab? SelectedTab
    {
        get => Get<SheetTab?>(null);
        set
        {
            if (!Set(value) || value is null || settings.Sheet is null || loading) return;
            if (value.Id == settings.Sheet.SourceChannelSheetId) { VideoTabError = "Chọn tab khác tab kênh nguồn; app giữ lựa chọn cũ."; loading = true; SelectedTab = SheetTabs.FirstOrDefault(t => t.Id == settings.Sheet.SheetId); loading = false; return; }
            SourceTabError = ""; VideoTabError = "";
            settings.Sheet = settings.Sheet with { SheetId = value.Id, TabName = value.Name, Columns = null };
            Notify(nameof(ColumnMappingLabel));
            Notify(nameof(SheetLabel)); PersistSettings();
            FullNiche = ""; _ = LoadNichesAsync();
        }
    }
    public ICommand SubmitFullCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand RemoveJobCommand { get; }
    public ICommand RefreshNichesCommand { get; }
    public ICommand ConnectSheetCommand { get; }
    public ICommand ConnectYouTubeCommand { get; }
    public ICommand ImportOAuthCommand { get; }
    public ICommand ConnectGoogleCommand { get; }
    public ICommand ChooseFullFolderCommand { get; }
    public ICommand OpenDownloadFolderCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand OpenSheetCommand { get; }
    public ICommand OpenJobSheetCommand { get; }
    public ICommand RefreshSheetTabsCommand { get; }
    public ICommand ChooseVideoColumnsCommand { get; }
    public ICommand ChooseChannelColumnsCommand { get; }
    public ICommand OpenChannelCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand OpenVideoCommand { get; }
    public ICommand DisconnectSheetCommand { get; }
    public ICommand DisconnectYouTubeCommand { get; }
    public ICommand OpenDataCommand { get; }
    public ICommand ReconnectJobCommand { get; }
    public ICommand ReconcileCommand { get; }
    public ICommand HelpCommand { get; }
    public ICommand DouyinHelpCommand { get; }
    public ICommand OpenDouyinSessionCommand { get; }
    public ICommand UserGuideCommand { get; }
    public MainViewModel(LocalStore store, ISecretStore secrets, GoogleAuth auth, GoogleSheets sheets, YouTubeDestination youtube, JobCoordinator coordinator, string dataRoot)
    {
        this.store = store; this.secrets = secrets; this.auth = auth; this.sheets = sheets; this.youtube = youtube; Coordinator = coordinator; this.dataRoot = dataRoot;
        foreach (var choice in ActionChoices) choice.PropertyChanged += (_, _) => FiltersChanged();
        foreach (var choice in StateChoices) choice.PropertyChanged += (_, _) => FiltersChanged();
        RefreshHistoryCommand = Command(async () => { await RefreshAsync(); Notice = "Đã làm mới lịch sử công việc."; }, independent: true);
        ClearStateFilterCommand = Command(async () => { foreach (var choice in StateChoices) choice.IsSelected = false; await RefreshAsync(); }, independent: true);
        ClearActionFilterCommand = Command(async () => { foreach (var choice in ActionChoices) choice.IsSelected = false; await RefreshAsync(); }, independent: true);
        ClearHistorySearchCommand = Command(async () => { HistorySearch = ""; await RefreshAsync(); }, independent: true);
        ResetHistoryFiltersCommand = Command(async () => { ResetHistoryFilters(); await RefreshAsync(); }, independent: true);
        SubmitFullCommand = Command(SubmitAsync, () => TryThreshold(out _) && settings.LastActions != JobActions.None && Filled(FullLink, FullName)
            && FullLinkError.Length == 0 && FullNameError.Length == 0 && UploadTitleError.Length == 0 && UploadDescriptionError.Length == 0
            && (!SheetSelected || settings.Sheet is not null && !NicheLoading && Niches.Contains(FullNiche)) && (!UploadSelected || settings.Channel is not null), OperationArea.Input);
        AddActionsCommand = Command(AddActionsAsync, () => SelectedJob?.CanSupplement == true, OperationArea.History);
        TogglePauseCommand = Command(async () => {
            var job = (await store.ListAsync()).Single(x => x.Id == SelectedJob!.Id);
            if (job.CanPause) { await Coordinator.PauseJobAsync(job.Id); await RefreshAsync(); Notice = "Đã tạm dừng mục được chọn. Các công việc khác tiếp tục chạy."; }
            else if (JobAttention.From(job) is not null) { PreferredAttentionJobId = job.Id; AttentionRequired?.Invoke(); }
            else if (job.CanResume) await ResumeAsync();
        }, () => SelectedJob?.CanTogglePause == true, OperationArea.History);
        RemoveJobCommand = Command(RemoveJobAsync, () => SelectedJob is not null, OperationArea.History);
        RefreshNichesCommand = Command(() => LoadNichesAsync(Ct), () => settings.Sheet is not null && !NicheLoading, independent: true);
        ImportOAuthCommand = Command(ImportOAuthAsync);
        ConnectGoogleCommand = Command(ConnectGoogleAsync);
        ConnectSheetCommand = Command(ConnectSheetAsync);
        ConnectYouTubeCommand = Command(ConnectYouTubeAsync);
        ChooseFullFolderCommand = Command(() => ChooseFolderAsync(), independent: true);
        OpenDownloadFolderCommand = Command(() => OpenAsync(SelectedJob!.DownloadDirectory ?? Path.GetDirectoryName(SelectedJob.Media!.Path)!),
            () => SelectedJob is not null && Directory.Exists(SelectedJob.DownloadDirectory ?? (SelectedJob.Media is not null ? Path.GetDirectoryName(SelectedJob.Media.Path) : null)), independent: true);
        StopCommand = new AsyncCommand(async () => {
            stoppingCommands = true; Busy = true;
            try { CancelOperations(); await Coordinator.StopAsync(); foreach (var pending in await store.ListAsync()) if (!pending.RemovedFromHistory && JobHistory.NeedsAttention(pending)) await Coordinator.PauseJobAsync(pending.Id); await RefreshAsync(); Notice = "Đã tạm dừng tất cả và lưu tiến độ."; }
            finally { stoppingCommands = false; Busy = operations.Count > 0; CommandManager.InvalidateRequerySuggested(); }
        }, () => Coordinator.IsRunning || Busy || HasAttentions, Error);
        OpenSheetCommand = Command(() => OpenAsync("https://docs.google.com/spreadsheets/d/" + settings.Sheet!.SpreadsheetId + "/edit#gid=" + settings.Sheet.SheetId), () => settings.Sheet is not null, independent: true);
        OpenJobSheetCommand = Command(() => OpenAsync("https://docs.google.com/spreadsheets/d/" + SelectedJob!.Sheet!.SpreadsheetId + "/edit#gid=" + SelectedJob.Sheet.SheetId), () => SelectedJob?.Sheet is not null, independent: true);
        RefreshSheetTabsCommand = Command(() => LoadSheetTabsAsync(Ct), () => settings.Sheet is not null, independent: true);
        ChooseVideoColumnsCommand = Command(() => ChooseColumnsAsync(false), () => settings.Sheet is not null);
        ChooseChannelColumnsCommand = Command(() => ChooseColumnsAsync(true), () => settings.Sheet?.SourceChannelSheetId is not null);
        OpenChannelCommand = Command(() => OpenAsync("https://www.youtube.com/channel/" + settings.Channel!.ChannelId), () => settings.Channel is not null, independent: true);
        OpenFileCommand = Command(() => OpenAsync(SelectedJob!.Media!.Path), () => SelectedJob?.Media is not null && File.Exists(SelectedJob.Media.Path), OperationArea.History);
        OpenVideoCommand = Command(() => OpenAsync("https://www.youtube.com/watch?v=" + SelectedJob!.YouTubeId), () => SelectedJob?.YouTubeId is not null, OperationArea.History);
        OpenDataCommand = Command(() => OpenAsync(dataRoot), independent: true);
        DisconnectSheetCommand = Command(async () => { await DisconnectAsync(settings.Sheet!.CredentialId); settings.Sheet = null; SheetTabs.Clear(); SelectedTab = null; await SaveSettingsAsync(); await LoadNichesAsync(); }, () => settings.Sheet is not null);
        DisconnectYouTubeCommand = Command(async () => { await DisconnectAsync(settings.Channel!.CredentialId); settings.Channel = null; await SaveSettingsAsync(); }, () => settings.Channel is not null);
        ReconnectJobCommand = Command(ReconnectJobAsync, () => SelectedJob?.NeedsGoogleReconnect == true, OperationArea.History);
        ReconcileCommand = Command(ReconcileAsync, () => SelectedJob?.NeedsReconciliation == true, OperationArea.History);
        HelpCommand = Command(ShowHelpAsync, independent: true);
        DouyinHelpCommand = Command(() => ShowFileHelpAsync("DOUYIN-SESSION.md", "Hướng dẫn phiên Douyin"), independent: true);
        OpenDouyinSessionCommand = Command(async () => {
            Notice = "Đang mở phiên Douyin; chờ lượt đọc nguồn hiện tại nếu có…";
            var authenticated = await Coordinator.OpenSourceSessionAsync(token => new DouyinBrowserReader(Path.Combine(dataRoot, "douyin-browser"), App.SessionDiagnosticOutput).OpenSessionAsync(token), Ct);
            Notice = authenticated == true ? "Douyin đã xác nhận đăng nhập; phiên được WebView2 giữ trong hồ sơ của app." : authenticated == false ? "Douyin báo chưa đăng nhập. Mở lại cửa sổ để đăng nhập/xác minh." : "Đã đóng Douyin; chưa nhận được xác nhận đăng nhập từ trang.";
        });
        UserGuideCommand = Command(() => ShowFileHelpAsync("README.md", "Hướng dẫn sử dụng — Tìm video hay"), independent: true);
        coordinator.Changed += job => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (job.State == JobState.Complete && announcedStates.TryGetValue(job.Id, out var previous) && previous != JobState.Complete)
                BackgroundNotice?.Invoke(job.Id, job.Name);
            announcedStates[job.Id] = job.State;
            live[job.Id] = job; dirty = true;
            CommandManager.InvalidateRequerySuggested();
        });
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        refresh.Tick += async (_, _) => { if (dirty) { dirty = false; await RefreshAsync(); } };
    }
    public async Task InitializeAsync()
    {
        settings = await store.SettingsAsync();
        settings.PendingOperations ??= [];
        if (settings.ShortMaxMilliseconds is < 1000 or > 86400000) settings.ShortMaxMilliseconds = 300000;
        if (settings.LastActions == JobActions.None) settings.LastActions = JobActions.All;
        lastValidActions = settings.LastActions;
        await UpdateGoogleConfigurationAsync();
        // Keep the configured tab immediately; refresh the actual list when Picker is used again.
        if (settings.Sheet is not null) { var tab = new SheetTab(settings.Sheet.SheetId, settings.Sheet.TabName); SheetTabs.Add(tab); SelectedTab = tab;
            if (settings.Sheet.SourceChannelSheetId is int id) { var channel = new SheetTab(id, settings.Sheet.SourceChannelTabName ?? "Kênh nguồn"); SheetTabs.Add(channel); SelectedSourceChannelTab = channel; } }
        loading = false;
        if (App.SessionDiagnosticOutput is not null) { SettingsChanged(); return; } // Diagnose the actual UI command without resuming or changing saved jobs.
        await Coordinator.InitializeAsync(); await RefreshAsync(); SettingsChanged(); refresh.Start();
        Notice = "Sẵn sàng. Chọn các phần muốn thực hiện; đăng YouTube cần tải video.";
        _ = LoadNichesAsync(); // A slow/offline Sheet must not delay opening standalone downloads.
        _ = LoadSheetTabsAsync();
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? can = null, OperationArea area = OperationArea.Settings, bool independent = false, [CallerArgumentExpression(nameof(action))] string context = "") => new(async () =>
    {
        var group = CommandGroup(area, independent);
        using var cancel = new CancellationTokenSource();
        operation.Value = cancel; operations.Add(cancel); if (group is not null) commandGroups.Add(group); Busy = true;
        try { await action(); }
        finally { operations.Remove(cancel); operation.Value = null; if (group is not null) commandGroups.Remove(group); Busy = stoppingCommands || operations.Count > 0; CommandManager.InvalidateRequerySuggested(); }
    }, () => !stoppingCommands && (CommandGroup(area, independent) is not { } group || !commandGroups.Contains(group)) && (can?.Invoke() ?? true), ex => Error(ex, area, context));
    private static string? CommandGroup(OperationArea area, bool independent) => independent || area == OperationArea.History ? null
        : area == OperationArea.Input ? "input" : "settings";
    private CancellationToken Ct => operation.Value?.Token ?? CancellationToken.None;
    private void CancelOperations() { foreach (var cancel in operations.ToArray()) cancel.Cancel(); }
    private async Task SubmitAsync()
    {
        _ = Rules.VideoName(FullName);
        if (SheetSelected && (NicheLoading || !Niches.Contains(FullNiche))) throw new ArgumentException("Chọn một ngách từ danh sách của bảng hiện tại.");
        var job = new VideoJob { Actions = settings.LastActions, Input = FullLink, Name = FullName, Niche = SheetSelected ? FullNiche : "", Title = UploadTitle,
            Description = UploadDescription, Folder = DownloadSelected ? settings.CurrentFolder : "", Sheet = SheetSelected ? settings.Sheet : null,
            Channel = UploadSelected ? settings.Channel : null, Privacy = settings.Privacy, ShortMaxMilliseconds = settings.ShortMaxMilliseconds };
        Notice = "Đang nhận diện và kiểm tra video trùng…";
        try { await Coordinator.SubmitAsync(job, ct: Ct); }
        catch (DuplicateJobException ex)
        {
            var old = ex.Existing;
            var detail = $"Video đã có công việc: {old.Name}\nNgày: {old.Created.ToLocalTime():dd/MM/yyyy HH:mm} · ID: {old.Source?.Id}\n{old.ResultsLabel}\nBảng: {old.Sheet?.SpreadsheetName} / {old.Sheet?.TabName}\nKênh: {old.Channel?.Name}\n\n";
            if (!old.RemovedFromHistory)
            {
                if (ActionConfirmationWindow.Ask("Video trùng", detail + "Mở công việc đã có để Bổ sung / thực hiện lại hoặc Tiếp tục?", "Mở công việc đã có"))
                { ResetHistoryFilters(); await RefreshAsync(); SelectedJob = Jobs.FirstOrDefault(x => x.Id == old.Id); }
                Notice = "Chưa tạo bản sao. Dữ liệu nhập được giữ."; return;
            }
            if (old.Results != JobActions.None || old.UnknownResult) { Notice = detail + "Công việc đã ẩn còn kết quả hoặc kết quả chưa rõ; chưa thể tạo bản sao. Kiểm tra bảng/kênh và kết quả cũ trước."; ReportOperation(Notice, OperationArea.History); return; }
            if (!ActionConfirmationWindow.Ask("Video từng xử lý", detail + "Kết quả cũ đã xóa hết. Tạo công việc thay thế?", "Tạo công việc thay thế")) return;
            await Coordinator.SubmitAsync(job, old.Id, Ct);
        }
        // Keep the selected niche for consecutive videos using the same sheet.
        FullLink = ""; FullName = ""; UploadTitle = ""; UploadDescription = "";
        Notice = "Đã thêm công việc. Bạn có thể nhập video tiếp theo.";
        
        await RefreshAsync(); SelectedJob = Jobs.FirstOrDefault(x => x.Id == job.Id);
    }
    private async Task AddActionsAsync()
    {
        var id = SelectedJob!.Id;
        var job = (await store.ListAsync()).Single(x => x.Id == id); Notice = job.Message;
        if (!job.CanSupplement) throw new NeedsActionException("Không có phần cần bổ sung; dùng Tiếp tục cho phần đang dở.");
        var dialog = new AddActionsWindow(job, settings, sheets, Ct, canRefreshCovers: () => !Coordinator.IsJobBusy(id)) { Owner = Application.Current.MainWindow, RefreshJob = async () => (await store.ListAsync()).Single(x => x.Id == id), CheckResults = token => Coordinator.RecheckResultsAsync(id, token), ChooseMovedFolder = () => ChooseMovedFolderAsync(id) };
        if (dialog.ShowDialog() != true) return;
        if (dialog.RefreshCovers) await Coordinator.RefreshCoversAsync(job.Id, Ct);
        if (dialog.Actions != JobActions.None) await Coordinator.AddActionsAsync(job.Id, dialog.Actions, settings, dialog.Niche, dialog.UseCurrentTargets);
        await RefreshAsync(); Notice = dialog.Actions != JobActions.None ? "Đã xếp hàng phần bổ sung; kết quả còn tồn tại được giữ." : SelectedJob?.Message ?? "Đã xử lý ảnh bìa.";
    }
    private async Task ImportOAuthAsync()
    {
        var dialog = new OpenFileDialog { Title = "Chọn OAuth client loại Desktop app", Filter = "Google OAuth client (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        await auth.ImportClientAsync(dialog.FileName); await UpdateGoogleConfigurationAsync(); Notice = "Đã lưu cấu hình Google. Bấm Kết nối Google để chọn bảng và kênh.";
    }
    private async Task UpdateGoogleConfigurationAsync()
    {
        GoogleConfigured = await auth.HasClientAsync();
        OAuthLabel = GoogleConfigured ? "Cấu hình Google đã sẵn sàng" : "Chưa có cấu hình Google";
        Notify(nameof(GoogleSetupMessage));
    }
    private async Task ConnectGoogleAsync()
    {
        TabIndex = 1;
        if (SheetSelected && !UploadSelected) { await ConnectSheetAsync(); return; }
        if (UploadSelected && !SheetSelected) { await ConnectYouTubeAsync(); return; }
        if (!SheetSelected && !UploadSelected) { Notice = "Tải video không cần kết nối Google."; return; }
        if (!GoogleConfigured) { Notice = GoogleSetupMessage; ReportOperation(Notice, OperationArea.GoogleClient); return; }
        var draft = new GoogleConnectionDraft(new GoogleConnectionService(auth, sheets, youtube), ApplyConnectionsAsync, settings.Sheet, settings.Channel);
        var wizard = new ConnectionWizardWindow(new(draft, Ct, sheets.ColumnOptionsAsync)) { Owner = Application.Current.MainWindow };
        Notice = "Hoàn thành ba bước trong cửa sổ Kết nối Google.";
        bool completed;
        try { completed = wizard.ShowDialog() == true; }
        finally { await draft.CancelAsync(); }
        if (completed) SetSheetTabs(draft.Tabs);
        Notice = completed ? "Đã lưu kết nối bảng và kênh. Bạn có thể bắt đầu xử lý video." : "Đã đóng trình hướng dẫn; kết nối cũ được giữ nếu chưa xác nhận.";
    }
    private async Task ConnectSheetAsync()
    {
        Notice = "Chọn đúng một bảng Google Sheets trong trình duyệt. App đang chờ Google tối đa 5 phút.";
        var selection = await new GoogleConnectionService(auth, sheets, youtube).SelectSheetAsync(Ct);
        var video = selection.Tabs.FirstOrDefault(t => settings.Sheet is { } prior && t.Id == prior.SheetId && selection.Id == prior.SpreadsheetId)
            ?? selection.Tabs.FirstOrDefault(t => t.Name != "Kênh nguồn") ?? selection.Tabs[0];
        var channel = selection.Tabs.FirstOrDefault(t => settings.Sheet is { } prior && t.Id == prior.SourceChannelSheetId && selection.Id == prior.SpreadsheetId)
            ?? selection.Tabs.FirstOrDefault(t => t.Name == "Kênh nguồn" && t.Id != video.Id);
        var previous = settings.Sheet;
        try { await ApplyConnectionsAsync(new(selection.Id, video.Id, selection.Name, video.Name, selection.CredentialId, channel?.Id, channel?.Name,
            previous?.SpreadsheetId == selection.Id && previous.SheetId == video.Id ? previous.Columns : null,
            previous?.SpreadsheetId == selection.Id && previous.SourceChannelSheetId == channel?.Id ? previous.SourceChannelColumns : null), settings.Channel, Ct); }
        catch { await auth.DisconnectLocalAsync(selection.CredentialId); throw; }
        SetSheetTabs(selection.Tabs); Notice = "Đã đổi bảng. Chọn đúng tab bên dưới; kết nối kênh YouTube được giữ nguyên.";
    }
    private async Task ConnectYouTubeAsync()
    {
        Notice = "Đăng nhập Google và chọn đúng kênh YouTube trong trình duyệt. App đang chờ Google.";
        var channel = await new GoogleConnectionService(auth, sheets, youtube).SelectChannelAsync(Ct);
        try { await ApplyConnectionsAsync(settings.Sheet, channel, Ct); }
        catch { await auth.DisconnectLocalAsync(channel.CredentialId); throw; }
        Notice = "Đã liên kết kênh " + channel.Name + ". Bảng hiện tại được giữ nguyên.";
    }
    private async Task ApplyConnectionsAsync(SheetTarget? sheet, ChannelTarget? channel, CancellationToken ct)
    {
        var next = new AppSettings { PendingOperations = settings.PendingOperations, VideoFolder = settings.CurrentFolder, LastActions = settings.LastActions, Privacy = settings.Privacy,
            Sheet = sheet, Channel = channel, ShortMaxMilliseconds = settings.ShortMaxMilliseconds };
        var sameSpreadsheet = settings.Sheet?.SpreadsheetId == sheet?.SpreadsheetId;
        var sameTab = sameSpreadsheet && settings.Sheet?.SheetId == sheet?.SheetId;
        await PersistSnapshotAsync(next, ct); settings = next;
        if (sheet is not null) SetSheetTabs(sameSpreadsheet && SheetTabs.Any(t => t.Id == sheet.SheetId && t.Name == sheet.TabName)
            ? SheetTabs.ToArray() : [new(sheet.SheetId, sheet.TabName)]);
        SettingsChanged();
        if (!sameTab) FullNiche = "";
        await LoadNichesAsync(ct);
    }
    private async Task LoadNichesAsync(CancellationToken ct = default)
    {
        var version = ++nicheLoadVersion;
        nicheLoad?.Cancel();
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct); nicheLoad = cancel;
        var target = SheetSelected ? settings.Sheet : null; var selected = FullNiche;
        Niches.Clear(); FullNiche = ""; NicheLoading = target is not null;
        NicheStatus = target is null ? "Chọn bảng để tải danh sách ngách." : "Đang tải ngách từ Google Sheets…";
        try
        {
            if (target is null) return;
            var options = await sheets.NichesAsync(target, cancel.Token);
            if (version != nicheLoadVersion || target != settings.Sheet) return;
            ResolveNicheNotices();
            foreach (var option in options) Niches.Add(option);
            if (Niches.Contains(selected)) FullNiche = selected;
            NicheStatus = $"{Niches.Count} ngách từ bảng hiện tại. Có thể gõ để tìm nhanh trong danh sách.";
        }
        catch (Exception ex)
        {
            if (version != nicheLoadVersion) return;
            NicheStatus = ex switch
            {
                NeedsAuthException => "Cần kết nối lại Google Sheets để lấy danh sách ngách.",
                System.Net.Http.HttpRequestException => "Chưa tải được ngách do lỗi mạng. Bấm Làm mới ngách để thử lại.",
                OperationCanceledException => "Đã dừng tải ngách. Bấm Làm mới ngách để thử lại.",
                NeedsActionException => ex.Message,
                _ => "Không đọc được dropdown Ngách. Kiểm tra bảng/tab rồi bấm Làm mới ngách."
            };
            if (ex is not OperationCanceledException) ReportOperation(NicheStatus, OperationArea.Niches, ex is NeedsAuthException);
        }
        finally
        {
            if (version == nicheLoadVersion) { NicheLoading = false; nicheLoad = null; InputChanged(); }
            cancel.Dispose();
        }
    }
    private void SetSheetTabs(IReadOnlyList<SheetTab> tabs)
    {
        var wasLoading = loading; loading = true; SheetTabs.Clear(); foreach (var tab in tabs) SheetTabs.Add(tab);
        SelectedTab = SheetTabs.FirstOrDefault(t => t.Id == settings.Sheet?.SheetId);
        if (settings.Sheet is not null && SelectedTab is { } video)
            settings.Sheet = settings.Sheet with { TabName = video.Name };
        SelectedSourceChannelTab = settings.Sheet?.SourceChannelSheetId is int id ? SheetTabs.FirstOrDefault(t => t.Id == id) : SheetTabs.FirstOrDefault(t => t.Name == "Kênh nguồn" && t.Id != settings.Sheet?.SheetId);
        if (settings.Sheet is not null && SelectedSourceChannelTab is { } channel)
            settings.Sheet = settings.Sheet with { SourceChannelSheetId = channel.Id, SourceChannelTabName = channel.Name };
        SourceTabError = ""; VideoTabError = "";
        loading = wasLoading; Notify(nameof(SheetLabel)); PersistSettings();
    }
    private async Task LoadSheetTabsAsync(CancellationToken ct = default, bool refreshNiches = true)
    {
        var target = settings.Sheet; if (target is null) return;
        SheetTabsStatus = "Đang tải danh sách tab…";
        try
        {
            var tabs = await sheets.TabsAsync(target, ct);
            if (settings.Sheet != target) return;
            SetSheetTabs(tabs);
            SheetTabsStatus = SelectedTab is null ? "Tab video đã chọn không còn tồn tại. Chọn lại tab video." : SelectedSourceChannelTab is null ? "Chọn tab kênh nguồn trong cùng file." : $"{tabs.Count} tab trong cùng file. Đích đã lưu của công việc cũ được giữ.";
            if (refreshNiches) await LoadNichesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { if (settings.Sheet == target) {
            SheetTabsStatus = ex is NeedsAuthException ? "Cần kết nối lại bảng để tải danh sách tab." : "Chưa tải được danh sách tab. Kiểm tra kết nối rồi bấm Làm mới tab.";
            ReportOperation(SheetTabsStatus, OperationArea.Sheets);
        } }
    }
    private async Task ChooseFolderAsync()
    {
        var current = settings.CurrentFolder;
        var dialog = new OpenFolderDialog { Title = "Thư mục video", InitialDirectory = Directory.Exists(current) ? current : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) };
        if (dialog.ShowDialog() != true) return;
        settings.VideoFolder = dialog.FolderName; await SaveSettingsAsync(); Notice = "Đã đổi thư mục cho công việc mới; công việc đã tạo giữ đường dẫn riêng.";
    }
    private async Task ChooseColumnsAsync(bool channel)
    {
        var original = settings.Sheet!;
        var target = channel ? original with { SheetId = original.SourceChannelSheetId!.Value, TabName = original.SourceChannelTabName ?? "Kênh nguồn", Columns = original.SourceChannelColumns } : original;
        var dialog = new SheetColumnsWindow(target.TabName, channel, Ct, (row, token) => sheets.ColumnOptionsAsync(target, channel, row, token)) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        if (settings.Sheet != original) throw new NeedsActionException("Đích bảng đã thay đổi; mở lại lựa chọn cột.");
        settings.Sheet = channel ? original with { SourceChannelColumns = dialog.Mapping } : original with { Columns = dialog.Mapping };
        await SaveSettingsAsync(); Notify(nameof(ColumnMappingLabel));
        if (!channel) await LoadNichesAsync(Ct);
        Notice = "Đã lưu cột cho công việc mới. Đổi tên tiêu đề vẫn dùng được; chèn/xóa/đổi thứ tự cột cần chọn lại.";
    }
    private Task ShowHelpAsync() => ShowFileHelpAsync("GOOGLE-SETUP.md", "Thiết lập Google — Tìm video hay");
    private async Task ShowFileHelpAsync(string fileName, string title)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, fileName), Ct);
        var box = new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), Padding = new Thickness(18), Margin = new Thickness(0) };
        var content = new System.Windows.Controls.DockPanel { Margin = new Thickness(18) };
        var searchBar = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var find = new System.Windows.Controls.Button { Content = "Tìm tiếp", Margin = new Thickness(8, 0, 0, 0) };
        System.Windows.Controls.DockPanel.SetDock(find, System.Windows.Controls.Dock.Right); searchBar.Children.Add(find);
        var search = new System.Windows.Controls.TextBox { Margin = new Thickness(0), ToolTip = "Nhập nội dung cần tìm trong hướng dẫn" };
        searchBar.Children.Add(search); System.Windows.Controls.DockPanel.SetDock(searchBar, System.Windows.Controls.Dock.Top); content.Children.Add(searchBar);
        var footer = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var close = new System.Windows.Controls.Button { Content = "Đóng hướng dẫn", HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
        System.Windows.Controls.DockPanel.SetDock(close, System.Windows.Controls.Dock.Right); footer.Children.Add(close);
        var found = new System.Windows.Controls.TextBlock { Text = "Ctrl+F để tìm trong hướng dẫn", VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(found); System.Windows.Controls.DockPanel.SetDock(footer, System.Windows.Controls.Dock.Bottom); content.Children.Add(footer); content.Children.Add(box);
        var help = new Window { Title = title, Width = 840, Height = 700, MinWidth = 600, MinHeight = 450, Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        help.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        help.MaxHeight = Math.Max(help.MinHeight, SystemParameters.WorkArea.Height - 32); help.Height = Math.Min(help.Height, help.MaxHeight);
        close.Click += (_, _) => help.Close();
        void FindNext()
        {
            if (string.IsNullOrWhiteSpace(search.Text)) { search.Focus(); return; }
            var start = box.SelectionStart + box.SelectionLength;
            var index = text.IndexOf(search.Text, Math.Min(start, text.Length), StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = text.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase);
            if (index < 0) { found.Text = "Không tìm thấy nội dung này."; return; }
            box.Focus(); box.Select(index, search.Text.Length); box.ScrollToLine(box.GetLineIndexFromCharacterIndex(index)); found.Text = "Đã tìm thấy. Bấm Tìm tiếp để tìm vị trí kế tiếp.";
        }
        find.Click += (_, _) => FindNext();
        search.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { FindNext(); e.Handled = true; } };
        help.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) { search.Focus(); search.SelectAll(); e.Handled = true; } };
        System.Windows.Automation.AutomationProperties.SetName(search, "Tìm trong hướng dẫn");
        help.Show();
    }
    private async Task ResumeAsync()
    {
        var id = SelectedJob!.Id;
        var job = (await store.ListAsync()).Single(x => x.Id == id);
        if (job.DeletionRequested && job.DeleteYouTubeRequested && !job.YouTubeDeleted) await EnsureDeletePermissionAsync(job);
        await Coordinator.ResumeAsync(job.Id); Notice = job.DeletionRequested ? "Đang tiếp tục xóa các kết quả đã chọn." : "Đang tiếp tục từ checkpoint và đối soát kết quả cũ.";
    }
    private async Task RemoveJobAsync()
    {
        var id = SelectedJob!.Id;
        var wasActive = SelectedJob.State is JobState.Running or JobState.Queued or JobState.WaitingForYouTube or JobState.WaitingToRetry;
        await Coordinator.PauseJobAsync(id);
        var job = (await store.ListAsync()).Single(x => x.Id == id); Notice = job.Message;
        var dialog = new DeleteJobWindow(job) { Owner = Application.Current.MainWindow, RefreshJob = async () => (await store.ListAsync()).Single(x => x.Id == id), CheckResults = token => Coordinator.RecheckResultsAsync(id, token), ChooseMovedFolder = () => ChooseMovedFolderAsync(id) };
        var confirmed = dialog.ShowDialog() == true;
        await dialog.WaitForChecksAsync();
        if (!confirmed)
        {
            if (wasActive) await Coordinator.ResumeAsync(id);
            Notice = "Đã hủy xóa công việc."; await RefreshAsync(); return;
        }
        await Coordinator.RemoveJobAsync(id, dialog.DeleteSheet, dialog.DeleteYouTube, dialog.DeleteFolder, dialog.RemoveHistory, dialog.ConfirmMissingFolder); await RefreshAsync();
        Notice = dialog.DeleteSheet || dialog.DeleteYouTube ? "Đang xóa các phần đã chọn ở nền. Kết quả lỗi được giữ để xử lý tiếp." : "Đã xử lý lựa chọn xóa trên máy; xem kết quả trong lịch sử.";
    }
    private async Task<VideoJob?> ChooseMovedFolderAsync(string id)
    {
        var picker = new OpenFolderDialog { Title = "Chọn đúng thư mục video đã chuyển (app sẽ đối chiếu file)", Multiselect = false };
        if (picker.ShowDialog(Application.Current.MainWindow) != true) return null;
        Notice = "Đang đối chiếu dung lượng và SHA-256 của file trong thư mục được chọn…";
        var job = await Coordinator.RelocateFolderAsync(id, picker.FolderName, Ct); Notice = job.Message;
        await RefreshAsync(); return job;
    }
    private async Task EnsureDeletePermissionAsync(VideoJob job)
    {
        var original = job.Channel ?? throw new NeedsActionException("Công việc không có kênh gốc.");
        if (await auth.HasYouTubeDeleteScopeAsync(original.CredentialId, Ct)) return;
        Notice = "Để xóa video, cần cấp thêm quyền YouTube một lần. Chọn đúng tài khoản/kênh gốc trên trang Google.";
        var granted = await auth.ConnectAsync(false, Ct, allowYouTubeDeletion: true);
        try
        {
            var channel = await youtube.ChannelAsync(granted.CredentialId, Ct);
            if (channel.ChannelId != original.ChannelId) throw new NeedsActionException("Kênh vừa cấp quyền khác kênh gốc. App chưa xóa video.");
            await auth.AdoptYouTubeDeleteGrantAsync(original.CredentialId, granted.CredentialId, Ct);
        }
        catch { await auth.DisconnectLocalAsync(granted.CredentialId); throw; }
    }
    private async Task DisconnectAsync(string key)
    {
        if (Coordinator.UsesCredential(key)) throw new NeedsActionException("Tạm dừng các công việc đang dùng kết nối này trước khi ngắt kết nối.");
        await auth.DisconnectLocalAsync(key); Notice = "Đã xóa kết nối lưu trên máy. Công việc dùng kết nối này sẽ yêu cầu đăng nhập lại.";
    }
    private Task ReconnectJobAsync() => ReconnectJobAsync(SelectedJob!.Id);
    private async Task ReconnectJobAsync(string id)
    {
        var job = (await store.ListAsync()).Single(x => x.Id == id);
        var previousSheet = job.Sheet; var previousChannel = job.Channel;
        var reconnectActions = job.Issue?.AffectedActions ?? job.Work;
        if (job.DeletionRequested && job.Issue is null) reconnectActions = (job.DeleteSheetRequested ? JobActions.WriteSheet : 0) | (job.DeleteYouTubeRequested ? JobActions.UploadYouTube : 0);
        Notice = "Chọn lại đúng bảng và đúng kênh của công việc. App sẽ kiểm tra ID trước khi thay quyền.";
        if (reconnectActions.HasFlag(JobActions.WriteSheet) && job.Sheet is not null)
        {
            var sheet = await sheets.SelectAsync(await auth.ConnectAsync(true, Ct), Ct);
            if (sheet.Id != job.Sheet.SpreadsheetId || sheet.Tabs.All(t => t.Id != job.Sheet.SheetId)) { await auth.DisconnectLocalAsync(sheet.CredentialId); throw new NeedsActionException("Bảng/tab khác đích gốc."); }
            await auth.AdoptVerifiedGrantAsync(job.Sheet.CredentialId, sheet.CredentialId, Ct);
        }
        if (reconnectActions.HasFlag(JobActions.UploadYouTube) && job.Channel is not null)
        {
            var requireDeletion = job.DeletionRequested && job.DeleteYouTubeRequested && !job.YouTubeDeleted
                || (await store.ListAsync()).Any(x => !x.RemovedFromHistory && x.State == JobState.NeedsAuth
                    && x.Channel?.CredentialId == job.Channel.CredentialId && x.Channel?.ChannelId == job.Channel.ChannelId
                    && x.DeletionRequested && x.DeleteYouTubeRequested && !x.YouTubeDeleted);
            var channel = await youtube.ChannelAsync((await auth.ConnectAsync(false, Ct, allowYouTubeDeletion: requireDeletion)).CredentialId, Ct);
            if (channel.ChannelId != job.Channel.ChannelId) { await auth.DisconnectLocalAsync(channel.CredentialId); throw new NeedsActionException("Kênh khác đích gốc."); }
            await auth.AdoptVerifiedGrantAsync(job.Channel.CredentialId, channel.CredentialId, Ct);
        }
        await store.SaveAsync(job);
        if (job.Issue?.Kind == JobIssueKind.Authentication)
        {
            var old = (await store.ListAsync()).Single(x => x.Id == id);
            // Preserve the previous credential identity for dependent jobs.
            old.Sheet = previousSheet; old.Channel = previousChannel;
            await Coordinator.ReconnectDependentJobsAsync(old, job);
        }
        else await Coordinator.ResumeAsync(id);
        dirty = true; Notice = "Đã kết nối lại đúng đích và tiếp tục các công việc liên quan.";
    }
    private async Task ReconcileAsync() { var id = SelectedJob!.Id; if (await ReconcileAsync(id)) await Coordinator.ResumeAsync(id); }
    private async Task<bool> ReconcileAsync(string id)
    {
        var job = (await store.ListAsync()).Single(x => x.Id == id);
        if (await Coordinator.InspectRemoteAsync(id, Ct)) { Notice = "Đã tìm lại kết quả hoặc phiên upload hợp lệ; tiếp tục xác nhận từ checkpoint."; return true; }
        job = (await store.ListAsync()).Single(x => x.Id == id);
        if (job.YouTubeId is not null) { await OpenAsync("https://studio.youtube.com/video/" + job.YouTubeId + "/edit"); Notice = "Đã mở video hiện có. Điều chỉnh/kiểm tra trên Studio rồi bấm Tiếp tục; app giữ ID này."; return true; }
        if (job.MediaSendStarted)
        {
            await OpenAsync("https://studio.youtube.com");
            var review = new UploadReviewWindow(job.Name);
            if (review.ShowDialog() != true) return false;
            if (review.VideoId is { } existing) { await Coordinator.AttachUploadedVideoAsync(id, existing, Ct); return true; }
            if (!review.ConfirmedAbsent) return false;
            if (job.UploadSessionKey is not null) await secrets.DeleteAsync(job.UploadSessionKey);
            await Coordinator.ConfirmUploadAbsentAsync(id, job.Issue?.Id); return true;
        }
        else if (job.SheetWriteStarted && !job.SheetVerified)
        {
            await OpenAsync("https://docs.google.com/spreadsheets/d/" + job.Sheet!.SpreadsheetId + "/edit#gid=" + job.Sheet.SheetId);
            var owner = Application.Current.Windows.OfType<AttentionWindow>().FirstOrDefault(x => x.IsVisible) ?? Application.Current.MainWindow; WindowAttention.Present(owner);
            if (!ActionConfirmationWindow.Ask("Kiểm tra kết quả ghi bảng", "Chỉ xác nhận sau khi chắc chắn bảng chưa có dòng cho video nguồn của công việc này. App vẫn tìm lại video trước khi ghi.\n\nLần ghi cũ chưa tạo dòng?", "Xác nhận chưa có dòng", owner)) return false;
            job.SheetWriteStarted = false; job.SheetRow = null;
        }
        else { Notice = "Dòng đã xác minh có thể đã bị xóa/sửa. Khôi phục dòng trong bảng rồi bấm Tiếp tục; không tự ghi bù."; return false; }
        await store.SaveAsync(job); dirty = true; Notice = "Đã ghi nhận đối soát của bạn."; return true;
    }
    private async Task RefreshAsync()
    {
        await refreshGate.WaitAsync();
        try {
        var selected = SelectedJob?.Id;
        var jobs = await store.ListAsync();
        var incoming = new List<VideoJob>();
        foreach (var stored in jobs.Where(x => !x.RemovedFromHistory))
        {
            var job = stored;
            if (live.TryGetValue(stored.Id, out var current) && current.State == JobState.Running)
            { job.Progress = current.Progress; job.ProgressAction = current.ProgressAction; job.DownloadedBytes = current.DownloadedBytes; job.DownloadTotalBytes = current.DownloadTotalBytes; job.UploadedBytes = current.UploadedBytes; }
            incoming.Add(job);
        }
        UpdateAttentions(incoming);
        var action = ActionChoices.Where(c => c.IsSelected).Aggregate(JobActions.None, (mask, c) => mask | c.Value);
        var states = StateChoices.Where(c => c.IsSelected).Select(c => c.Value).ToArray();
        var order = SortFilter switch { "Ngày tạo: mới nhất" => HistoryOrder.Newest, "Ngày tạo: cũ nhất" => HistoryOrder.Oldest, _ => HistoryOrder.Workflow };
        MergeHistory(Jobs, JobHistory.ProjectSelected(incoming, action, states, order, HistorySearch, requireAllActions: true, includeEmptyActions: ActionChoices.Any(c => c.Value == JobActions.None && c.IsSelected)));
        HistorySummary = $"Hiển thị {Jobs.Count}/{incoming.Count} · Đang chạy {incoming.Count(j => j.State == JobState.Running)} · Hàng chờ {incoming.Count(j => j.State == JobState.Queued)} · Chờ tự xử lý {incoming.Count(j => j.State is JobState.WaitingForYouTube or JobState.WaitingToRetry)} · Cần xử lý {incoming.Count(JobHistory.NeedsAttention)} · Tạm dừng {incoming.Count(j => j.State == JobState.Paused)} · Hoàn tất {incoming.Count(j => j.State == JobState.Complete)}";
        SelectedJob = Jobs.FirstOrDefault(x => x.Id == selected);
        } finally { refreshGate.Release(); }
    }
    private void ResetHistoryFilters()
    {
        foreach (var choice in ActionChoices) choice.IsSelected = false;
        foreach (var choice in StateChoices) choice.IsSelected = false;
        SortFilter = SortFilters[0]; HistorySearch = "";
    }
    private void MergeHistory(ObservableCollection<VideoJob> history, VideoJob[] incoming)
    {
        var ids = incoming.Select(x => x.Id).ToHashSet();
        for (var index = history.Count - 1; index >= 0; index--)
            if (!ids.Contains(history[index].Id)) { historySnapshots.Remove(history[index].Id); history.RemoveAt(index); }
        for (var index = 0; index < incoming.Length; index++)
        {
            var job = incoming[index];
            var snapshot = System.Text.Json.JsonSerializer.Serialize(job, LocalStore.Json) + ":" + job.QueuePosition;
            var existing = history.FirstOrDefault(x => x.Id == job.Id);
            if (existing is null) history.Insert(index, job);
            else
            {
                var previous = history.IndexOf(existing);
                if (previous != index) history.Move(previous, index);
                if (historySnapshots.GetValueOrDefault(job.Id) != snapshot) history[index] = job;
            }
            historySnapshots[job.Id] = snapshot;
        }
    }
    internal async Task CheckThresholdSavedAsync(long expected)
    {
        await SaveSettingsAsync();
        if ((await store.SettingsAsync()).ShortMaxMilliseconds != expected) throw new InvalidOperationException("Threshold did not persist.");
    }
    internal async Task<object> CheckSelectionPreferencesAsync()
    {
        var previous = settings.LastActions;
        UploadSelected = false; SheetSelected = false; DownloadSelected = true; DownloadSelected = false;
        await SaveSettingsAsync();
        if (settings.LastActions != JobActions.None || (await store.SettingsAsync()).LastActions != JobActions.Download) throw new InvalidOperationException("Last valid selection was not preserved.");
        settings.LastActions = previous; lastValidActions = previous; await SaveSettingsAsync();
        return new { lastValidSelectionRemembered = true };
    }
    internal AddActionsWindow CreateAddSmoke(VideoJob job, Func<SheetTarget, CancellationToken, Task<IReadOnlyList<string>>> reader)
        => new(job, new AppSettings { VideoFolder = dataRoot, Sheet = new("mock-sheet", 7, "Bảng thử", "Video", "mock-sheet-key"), Channel = new("mock-channel", "Kênh thử", "mock-channel-key") }, sheets, CancellationToken.None, reader);
    internal void SetTabsForSmoke(bool enabled)
    {
        loading = true;
        settings.Sheet = enabled ? new("mock-sheet", 7, "Hai tab minh họa", "Video", "mock", 8, "Kênh nguồn") : null;
        SetSheetTabs(enabled ? [new(7, "Video"), new(8, "Kênh nguồn")] : []);
        loading = false;
        if (enabled && (SelectedTab?.Id != 7 || SelectedSourceChannelTab?.Id != 8)) throw new InvalidOperationException("Two tab selectors not restored.");
        SheetTabsStatus = enabled ? "Hai tab minh họa; chưa đọc hoặc ghi Google thật." : "";
    }
    internal async Task<object> CheckUnifiedHistoryAsync()
    {
        var first = new VideoJob { Mode = JobMode.Full, Name = "Lịch sử cũ xử lý", State = JobState.Paused };
        var second = new VideoJob { Mode = JobMode.DownloadOnly, Name = "Lịch sử cũ tải", State = JobState.Paused };
        await store.SaveAsync(first); await store.SaveAsync(second); await RefreshAsync();
        if (Jobs.Count != 2) throw new InvalidOperationException("Unified history failed.");
        first.RemovedFromHistory = true; second.RemovedFromHistory = true; await store.SaveAsync(first); await store.SaveAsync(second); await RefreshAsync();
        return new { unifiedLegacyHistory = true };
    }
    internal async Task<object> CheckHistorySmokeAsync(Func<Task> capture, Func<Task> selectFilterControls, Func<Task> refreshInformation)
    {
        var timestamp = DateTimeOffset.UtcNow.AddDays(-1);
        var seeded = Enumerable.Range(1, 120).Select(i => new VideoJob { Name = $"Hàng chờ {i:000}", Niche = i % 2 == 0 ? "Vũ trụ" : "Vật lý",
            Actions = i % 3 == 0 ? JobActions.Download : i % 3 == 1 ? JobActions.WriteSheet : JobActions.All,
            State = JobState.Queued, Created = timestamp.AddMinutes(i), EnqueuedAt = timestamp.AddSeconds(i), Stage = "Chờ xử lý" }).ToList();
        seeded.AddRange(new[] { JobState.Running, JobState.WaitingForYouTube, JobState.NeedsAuth, JobState.NeedsAction, JobState.Failed, JobState.Paused, JobState.Complete }
            .Select(state => new VideoJob { Name = "Minh họa " + state, Actions = JobActions.Download, State = state, PendingActions = JobActions.None, Created = timestamp }));
        try
        {
            foreach (var job in seeded) await store.SaveAsync(job);
            ResetHistoryFilters(); var refreshClock = Stopwatch.StartNew(); await RefreshAsync(); var refreshMilliseconds = refreshClock.Elapsed.TotalMilliseconds;
            if (Jobs.Count != 127 || Jobs[0].State != JobState.Running || !Jobs.Where(j => j.State == JobState.Queued).Select(j => j.QueuePosition).SequenceEqual(Enumerable.Range(1, 120)))
                throw new InvalidOperationException("Many-job workflow order failed.");
            SelectedJob = Jobs.Single(j => j.Name == "Hàng chờ 080");
            await capture();
            await selectFilterControls(); await RefreshAsync();
            if (Jobs.Count != 1 || SelectedJob?.Name != "Hàng chờ 080" || SelectedJob.QueuePosition != 80)
                throw new InvalidOperationException("Search/state/action controls or global queue position failed.");
            HistorySearch = ""; foreach (var choice in ActionChoices) choice.IsSelected = false; SortFilter = SortFilters[1]; await RefreshAsync();
            if (Jobs.Count != 120 || Jobs[0].Name != "Hàng chờ 120" || SelectedJob?.Name != "Hàng chờ 080" || SelectedJob.QueuePosition != 80)
                throw new InvalidOperationException("Display sorting changed selection or execution order.");
            foreach (var choice in StateChoices) choice.IsSelected = choice.Value == HistoryState.Attention; await RefreshAsync(); if (Jobs.Count != 3) throw new InvalidOperationException("Attention filter failed.");
            ResetHistoryFilters();
            StateChoices.Single(c => c.Value == HistoryState.Queued).IsSelected = true;
            StateChoices.Single(c => c.Value == HistoryState.Paused).IsSelected = true;
            ActionChoices.Single(c => c.Value == JobActions.Download).IsSelected = true;
            ActionChoices.Single(c => c.Value == JobActions.WriteSheet).IsSelected = true;
            await RefreshAsync(); if (Jobs.Count != 40) throw new InvalidOperationException("Multiple actions must always require ALL; states use OR.");
            ResetHistoryFilters(); await RefreshAsync();
            var completed = seeded.Single(j => j.State == JobState.Complete); SelectedJob = Jobs.Single(j => j.Id == completed.Id);
            completed.Actions = JobActions.All; await store.SaveAsync(completed); await RefreshAsync();
            if (SelectedJob?.ModeLabel != "Tải + Bảng + YouTube") throw new InvalidOperationException("Supplemented action column stale.");
            completed.SheetDeleted = true; completed.YouTubeDeleted = true; await store.SaveAsync(completed); await RefreshAsync();
            if (SelectedJob?.ModeLabel != "Tải") throw new InvalidOperationException("Deleted action column stale.");
            completed.FolderDeleted = true; await store.SaveAsync(completed); await RefreshAsync();
            if (SelectedJob?.CurrentActions != JobActions.None || SelectedJob.ModeLabel != "") throw new InvalidOperationException("All deleted actions still shown.");
            ActionChoices.Single(c => c.Value == JobActions.Download).IsSelected = true; await RefreshAsync(); if (Jobs.Any(j => j.Id == completed.Id)) throw new InvalidOperationException("Deleted actions still match filter.");
            ResetHistoryFilters(); ActionChoices.Single(c => c.Value == JobActions.None).IsSelected = true; await RefreshAsync();
            if (Jobs.Count != 1 || Jobs[0].Id != completed.Id || Jobs[0].ModeLabel != "") throw new InvalidOperationException("Empty work filter did not match the blank cell.");
            ActionChoices.Single(c => c.Value == JobActions.Download).IsSelected = true; await RefreshAsync();
            if (!Jobs.Any(j => j.Id == completed.Id) || !Jobs.Any(j => j.CurrentActions.HasFlag(JobActions.Download))) throw new InvalidOperationException("Combined empty/action filter failed.");
            // Queue position is display-only and must refresh even when stored payload is unchanged.
            ResetHistoryFilters(); await RefreshAsync(); SelectedJob = Jobs.Single(j => j.Name == "Hàng chờ 080");
            seeded[0].RemovedFromHistory = true; await store.SaveAsync(seeded[0]); await RefreshAsync();
            if (SelectedJob?.QueuePosition != 79) throw new InvalidOperationException("Queue-only snapshot update was lost.");
            var refreshJob = seeded.Single(j => j.State == JobState.Paused);
            SelectedJob = Jobs.Single(j => j.Id == refreshJob.Id); refreshJob.Stage = "Thông tin mới từ kho dữ liệu";
            refreshJob.Progress = 0; refreshJob.ProgressAction = JobActions.Download; refreshJob.DownloadedBytes = 438; refreshJob.DownloadTotalBytes = 1000;
            await store.SaveAsync(refreshJob);
            SetTabsForSmoke(true); Niches.Add("Ngách giữ nguyên"); FullNiche = "Ngách giữ nguyên"; NicheStatus = "Không làm mới ngách";
            var tabStatus = SheetTabsStatus;
            var formExpanded = NewJobExpanded; var detailExpanded = DetailsExpanded;
            await refreshInformation();
            if (SheetTabsStatus != tabStatus || Niches.Single() != "Ngách giữ nguyên" || FullNiche != "Ngách giữ nguyên" || NicheStatus != "Không làm mới ngách" || NewJobExpanded != formExpanded || DetailsExpanded != detailExpanded)
                throw new InvalidOperationException("History refresh changed app settings, niche data or expansion state.");
            SetTabsForSmoke(false); Niches.Clear(); FullNiche = "";
            if (Busy || SelectedJob?.Stage != refreshJob.Stage || !Notice.StartsWith("Đã làm mới")) throw new InvalidOperationException("Refresh history command failed.");
            if (SelectedJob.DisplayProgress != 43.8 || SelectedJob.State != JobState.Paused) throw new InvalidOperationException("History refresh lost the saved paused byte progress.");
            return new { historyRefreshButtonVerified = true, historyRefreshIsLocalOnly = true, queuedJobs = 120, totalJobs = 127, actionChangesReflected = true, deletedActionsExcludedFromFilter = true, blankWorkCellAndFilterVerified = true,
                globalQueuePositionsPreservedByFilters = true, queueOnlyChangesRefreshed = true, selectionSurvivesSorting = true, pausedProgressSurvivesHistoryRefresh = true,
                multipleFilterChoicesBound = true, allSelectedActionsRequired = true, searchAndFilterControlsBound = true, attentionStatesGrouped = true, historyRefreshMilliseconds = refreshMilliseconds,
                diagnostic = "Actual WPF and SQLite fixture; no real jobs or Google calls" };
        }
        finally
        {
            foreach (var job in seeded) { job.RemovedFromHistory = true; await store.SaveAsync(job); }
            ResetHistoryFilters(); await RefreshAsync();
        }
    }
    private void PersistSettings() { if (!loading) _ = PersistSafeAsync(); }
    private async Task PersistSafeAsync() { try { await SaveSettingsAsync(); } catch (Exception ex) { Error(ex); } }
    private Task PersistSnapshotAsync(AppSettings current, CancellationToken ct = default)
    {
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(current, LocalStore.Json), LocalStore.Json)!;
        if (snapshot.LastActions == JobActions.None) snapshot.LastActions = lastValidActions;
        return store.SaveSettingsAsync(snapshot, ct);
    }
    private async Task SaveSettingsAsync() { await PersistSnapshotAsync(settings); SettingsChanged(); }
    private void SettingsChanged() { InputChanged(); foreach (var p in new[] { nameof(SheetLabel), nameof(ColumnMappingLabel), nameof(ThresholdStatus), nameof(ShortMinutes), nameof(ChannelLabel), nameof(FolderLabel), nameof(DownloadSelected), nameof(SheetSelected), nameof(UploadSelected), nameof(DownloadEditable), nameof(Privacy), nameof(PrivacyLabel) }) Notify(p); CommandManager.InvalidateRequerySuggested(); }
    public async Task ShutdownAsync() { stoppingCommands = true; Busy = true; refresh.Stop(); nicheLoad?.Cancel(); CancelOperations(); await Coordinator.StopAsync(preservePendingYouTube: true); await PersistSnapshotAsync(settings); }
    internal void CancelSessionDiagnostic() => CancelOperations();
    private static bool Filled(params string[] fields) => fields.All(x => !string.IsNullOrWhiteSpace(x));
    private static Task OpenAsync(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return Task.CompletedTask; }
    private void Error(Exception ex) => Error(ex, OperationArea.General);
    private void Error(Exception ex, OperationArea area, string context = "") {
        if (area == OperationArea.Settings) area = context.Contains("ConnectSheet") ? OperationArea.Sheets
            : context.Contains("ConnectYouTube") ? OperationArea.YouTube : context.Contains("ImportOAuth") ? OperationArea.GoogleClient
            : context.Contains("ChooseFolder") ? OperationArea.Storage : area;
        Notice = ex switch {
        OperationCanceledException => "Đã hủy thao tác đang chờ.",
        System.Net.Http.HttpRequestException => "Kết nối mạng bị lỗi. Kiểm tra mạng rồi thử lại.",
        NeedsActionException or NeedsAuthException or ArgumentException => ex.Message,
        _ => "Không hoàn tất được thao tác. Kiểm tra bản cài và quyền thư mục dữ liệu. Loại lỗi: " + ex.GetType().Name }; if (ex is not OperationCanceledException) ReportOperation(OperationTitle(context) + Notice, area); }
    private T Get<T>(T fallback, [CallerMemberName] string name = "") => values.TryGetValue(name, out var value) ? (T)value! : fallback;
    private bool Set<T>(T value, [CallerMemberName] string name = "") { if (values.TryGetValue(name, out var old) && Equals(old, value)) return false; values[name] = value; Notify(name); CommandManager.InvalidateRequerySuggested(); return true; }
    private void Notify([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class AsyncCommand(Func<Task> action, Func<bool> can, Action<Exception> error) : ICommand
{
    private bool executing;
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => !executing && can();
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return; executing = true; CommandManager.InvalidateRequerySuggested();
        try { await action(); } catch (Exception ex) { error(ex); }
        finally { executing = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
