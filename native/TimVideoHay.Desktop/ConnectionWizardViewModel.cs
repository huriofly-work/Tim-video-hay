using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TimVideoHay.Core;

namespace TimVideoHay.Desktop;

public sealed class ConnectionWizardViewModel : INotifyPropertyChanged
{
    private readonly GoogleConnectionDraft draft;
    private readonly CancellationToken parentToken;
    private CancellationTokenSource? operation;
    private Task operationTask = Task.CompletedTask;
    private bool busy;
    private string notice = "Chọn bảng cần ghi dữ liệu. Đăng nhập và cấp quyền trên trang Google mở trong trình duyệt.";
    private SheetTab? selectedTab;
    private SheetTab? selectedSourceTab;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? AttentionRequested;
    private bool noticeIsError;
    public bool NoticeIsError { get => noticeIsError; private set { noticeIsError = value; Notify(); } }
    public event Action? Completed;
    public event Action? CancelRequested;
    public ObservableCollection<SheetTab> Tabs { get; } = new();
    public bool Busy { get => busy; private set { busy = value; Notify(); CommandManager.InvalidateRequerySuggested(); } }
    public string Notice { get => notice; private set { notice = value; NoticeIsError = false; Notify(); } }
    public bool IsSheet => draft.Step == ConnectionStep.Sheet;
    public bool IsChannel => draft.Step == ConnectionStep.Channel;
    public bool IsConfirm => draft.Step == ConnectionStep.Confirm;
    public bool CanBack => !IsSheet;
    public bool HasSheet => draft.Sheet is not null;
    public bool HasChannel => draft.Channel is not null;
    public string Heading => draft.Step switch { ConnectionStep.Sheet => "1. Chọn bảng và tab dữ liệu", ConnectionStep.Channel => "2. Chọn kênh YouTube", _ => "3. Xác nhận kết nối" };
    public string SheetName => draft.Sheet?.SpreadsheetName ?? "Chưa chọn bảng";
    public string TabName => draft.Sheet?.TabName ?? "";
    public string SourceTabName => draft.Sheet?.SourceChannelTabName ?? "";
    public string ChannelName => draft.Channel?.Name ?? "Chưa chọn kênh";
    public SheetTab? SelectedTab { get => selectedTab; set { selectedTab = value; Notify(); CommandManager.InvalidateRequerySuggested(); } }
    public SheetTab? SelectedSourceTab { get => selectedSourceTab; set { selectedSourceTab = value; Notify(); CommandManager.InvalidateRequerySuggested(); } }
    public ICommand SelectSheetCommand { get; }
    public ICommand SelectChannelCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenChannelCommand { get; }
    public ICommand ChooseVideoColumnsCommand { get; }
    public ICommand ChooseChannelColumnsCommand { get; }

    public ConnectionWizardViewModel(GoogleConnectionDraft draft, CancellationToken parentToken, Func<SheetTarget, bool, int?, CancellationToken, Task<SheetColumnSelection>>? columnReader = null)
    {
        this.draft = draft; this.parentToken = parentToken; Update();
        async Task ChooseColumns(bool channel, CancellationToken token)
        {
            var tab = (channel ? SelectedSourceTab : SelectedTab)!; var current = draft.Sheet!;
            var target = current with { SheetId = tab.Id, TabName = tab.Name, Columns = channel ? current.SourceChannelSheetId == tab.Id ? current.SourceChannelColumns : null : current.SheetId == tab.Id ? current.Columns : null };
            var dialog = new SheetColumnsWindow(tab.Name, channel, token, (row, ct) => columnReader!(target, channel, row, ct)) { Owner = System.Windows.Application.Current.Windows.OfType<ConnectionWizardWindow>().FirstOrDefault() ?? System.Windows.Application.Current.MainWindow };
            if (dialog.ShowDialog() == true) { draft.SetColumns(tab.Id, dialog.Mapping, channel); Notice = "Đã chọn cột; kết nối chỉ được lưu khi xác nhận cuối."; }
            await Task.CompletedTask;
        }
        ChooseVideoColumnsCommand = Command(ct => ChooseColumns(false, ct), () => columnReader is not null && HasSheet && SelectedTab is not null);
        ChooseChannelColumnsCommand = Command(ct => ChooseColumns(true, ct), () => columnReader is not null && HasSheet && SelectedSourceTab is not null);
        SelectSheetCommand = Command(async ct => {
            Notice = "Chọn một bảng Google Sheets trong trình duyệt. App sẽ chờ tối đa 5 phút.";
            await draft.SelectSheetAsync(ct); UpdateTabs(); Notice = "Đã chọn bảng. Chọn đúng tab dữ liệu rồi bấm Tiếp tục.";
        });
        SelectChannelCommand = Command(async ct => {
            Notice = "Chọn tài khoản hoặc kênh thương hiệu cần đăng trên trang Google.";
            await draft.SelectChannelAsync(ct); Notice = "Kiểm tra tên kênh đã xác minh bên dưới rồi bấm Tiếp tục.";
        });
        NextCommand = Command(async ct => {
            if (IsSheet) { Notice = "Đang kiểm tra tab video và lưu tab kênh nguồn…"; await draft.ContinueSheetAsync(SelectedTab!.Id, ct, SelectedSourceTab!.Id); Notice = "Chọn kênh YouTube sẽ nhận video."; }
            else { draft.ContinueChannel(); Notice = "Kiểm tra bảng, tab và kênh trước khi xác nhận."; }
        }, () => IsSheet ? SelectedTab is not null && SelectedSourceTab is not null && SelectedTab.Id != SelectedSourceTab.Id : IsChannel && HasChannel);
        BackCommand = Command(_ => { draft.Back(); UpdateTabs(); Notice = "Bạn có thể đổi lựa chọn trước khi xác nhận."; return Task.CompletedTask; }, () => CanBack);
        ConfirmCommand = Command(async ct => { await draft.CommitAsync(ct); Completed?.Invoke(); }, () => IsConfirm);
        CancelCommand = new AsyncCommand(() => { CancelRequested?.Invoke(); return Task.CompletedTask; }, () => true, Error);
        OpenChannelCommand = Command(_ => { Process.Start(new ProcessStartInfo("https://www.youtube.com/channel/" + draft.Channel!.ChannelId) { UseShellExecute = true }); return Task.CompletedTask; }, () => HasChannel);
    }
    private ICommand Command(Func<CancellationToken, Task> action, Func<bool>? can = null) => new AsyncCommand(async () => {
        Busy = true; operation = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        try { operationTask = action(operation.Token); await operationTask; }
        finally { operation.Dispose(); operation = null; Busy = false; Update(); }
    }, () => !Busy && (can?.Invoke() ?? true), Error);
    public async Task CancelAsync()
    {
        operation?.Cancel();
        try { await operationTask; } catch (Exception) { /* The command already reports the error in the dialog. */ }
        await draft.CancelAsync();
    }
    public void Error(Exception error)
    {
        Notice = error is OperationCanceledException ? "Đã hủy thao tác đang chờ." : error is System.Net.Http.HttpRequestException ? "Kết nối Google bị lỗi mạng. Kiểm tra mạng rồi bấm lại nút chọn bảng/kênh." : error.Message;
        NoticeIsError = error is not OperationCanceledException;
        if (NoticeIsError) AttentionRequested?.Invoke();
    }
    private void UpdateTabs()
    {
        Tabs.Clear(); foreach (var tab in draft.Tabs) Tabs.Add(tab);
        SelectedTab = System.Linq.Enumerable.FirstOrDefault(Tabs, t => t.Id == draft.Sheet?.SheetId);
        SelectedSourceTab = System.Linq.Enumerable.FirstOrDefault(Tabs, t => t.Id == draft.Sheet?.SourceChannelSheetId)
            ?? System.Linq.Enumerable.FirstOrDefault(Tabs, t => t.Name == "Kênh nguồn" && t.Id != SelectedTab?.Id);
    }
    private void Update()
    {
        if (Tabs.Count == 0) UpdateTabs();
        foreach (var property in new[] { nameof(IsSheet), nameof(IsChannel), nameof(IsConfirm), nameof(CanBack), nameof(HasSheet), nameof(HasChannel), nameof(Heading), nameof(SheetName), nameof(TabName), nameof(SourceTabName), nameof(ChannelName) }) Notify(property);
    }
    private void Notify([CallerMemberName] string property = "") => PropertyChanged?.Invoke(this, new(property));
}
