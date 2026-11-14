using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TimVideoHay.Core;

namespace TimVideoHay.Desktop;
public sealed partial class MainViewModel
{
    private IReadOnlyList<JobAttention> jobAttentions = [];
    public event Action<OperationArea>? FocusRequested;
    public event Action<string, string>? BackgroundNotice;
    public string VideoTabError { get => Get(""); private set => Set(value); }
    public string SourceTabError { get => Get(""); private set => Set(value); }
    public string FullLinkError { get { if (string.IsNullOrWhiteSpace(FullLink)) return ""; try { _ = Rules.DouyinUri(FullLink); return ""; } catch (ArgumentException e) { return e.Message; } } }
    public string FullNameError { get { if (string.IsNullOrEmpty(FullName)) return ""; try { _ = Rules.VideoName(FullName); return ""; } catch (ArgumentException e) { return e.Message; } } }
    public bool HasThresholdError => !TryThreshold(out _);
    public string NewJobHint => settings.LastActions == JobActions.None ? "Chọn ít nhất một công việc."
        : string.IsNullOrWhiteSpace(FullLink) ? "Nhập link Douyin để bắt đầu."
        : FullLinkError.Length > 0 ? FullLinkError : string.IsNullOrWhiteSpace(FullName) ? "Nhập tên video (tối đa 30 ký tự)."
        : FullNameError.Length > 0 ? FullNameError : UploadTitleError.Length > 0 ? UploadTitleError : UploadDescriptionError.Length > 0 ? UploadDescriptionError : !TryThreshold(out _) ? "Sửa mốc thời lượng trong Kết nối & cài đặt."
        : SheetSelected && settings.Sheet is null ? "Kết nối Google Sheets trong Kết nối & cài đặt."
        : SheetSelected && (NicheLoading || !Niches.Contains(FullNiche)) ? "Chọn ngách từ danh sách của bảng."
        : UploadSelected && settings.Channel is null ? "Kết nối kênh YouTube trong Kết nối & cài đặt." : "Sẵn sàng tạo công việc.";
    public string UploadTitleError { get { if (!UploadSelected) return ""; var title = string.IsNullOrWhiteSpace(UploadTitle) ? FullName : UploadTitle;
        if (string.IsNullOrWhiteSpace(title)) return "";
        try { _ = Rules.Text(title, "Tiêu đề YouTube", 100); return title.Contains('<') || title.Contains('>') ? "Tiêu đề YouTube không được chứa < hoặc >." : ""; }
        catch (ArgumentException ex) { return ex.Message; } } }
    public string UploadDescriptionError => UploadSelected && (System.Text.Encoding.UTF8.GetByteCount(UploadDescription) > 5000 || UploadDescription.Contains('<') || UploadDescription.Contains('>')) ? "Mô tả tối đa 5.000 byte UTF-8 và không chứa < hoặc >." : "";
    private void InputChanged()
    {
        Notify(nameof(FullLinkError)); Notify(nameof(FullNameError)); Notify(nameof(NewJobHint));
        Notify(nameof(HasThresholdError)); Notify(nameof(UploadTitleError)); Notify(nameof(UploadDescriptionError));
    }
    private void MergeNotifications()
    {
        var incoming = jobAttentions.Concat(settings.PendingOperations.Select(x => x.ToAttention())).ToArray();
        for (var i = Attentions.Count - 1; i >= 0; i--) if (!incoming.Any(x => x.JobId == Attentions[i].JobId)) Attentions.RemoveAt(i);
        foreach (var item in incoming)
        {
            var old = Attentions.FirstOrDefault(x => x.JobId == item.JobId);
            if (old is null) Attentions.Add(item);
            else if (old.Signature != item.Signature || old.Name != item.Name || old.Progress != item.Progress || old.Results != item.Results || old.Occurrences != item.Occurrences || !old.RelatedJobIds.SequenceEqual(item.RelatedJobIds)) Attentions[Attentions.IndexOf(old)] = item;
        }
        Notify(nameof(AttentionLabel)); Notify(nameof(HasAttentions));
    }
    private static string OperationTitle(string context) => context.Contains("ConnectSheet") || context.Contains("LoadSheetTabs") ? "Google Sheets: "
        : context.Contains("ConnectYouTube") ? "Kênh YouTube: " : context.Contains("ImportClient") || context.Contains("ImportOAuth") ? "Cấu hình Google: "
        : context.Contains("AddActions") ? "Bổ sung công việc: " : context.Contains("OpenAsync") ? "Mở kết quả: " : context.Contains("Reconnect") ? "Cấp lại quyền: " : "";
    private void ReportOperation(string message, OperationArea area, bool needsAuthentication = false)
    {
        var exists = settings.PendingOperations.Any(x => x.Message == message && x.Area == area);
        OperationNotice.Report(settings.PendingOperations, message, area, needsAuthentication); MergeNotifications();
        if (!loading) _ = SaveNoticesAsync();
        if (!exists) AttentionRequired?.Invoke();
    }
    private async Task SaveNoticesAsync()
    {
        try { await store.SavePendingOperationsAsync(settings.PendingOperations); }
        catch {
            const string failure = "Chưa lưu được yêu cầu xuống ổ đĩa. Yêu cầu vẫn còn trong phiên app này; kiểm tra quyền ghi/dung lượng thư mục dữ liệu.";
            Notice = failure;
            var known = settings.PendingOperations.Any(x => x.Message == failure);
            OperationNotice.Report(settings.PendingOperations, failure, OperationArea.General); MergeNotifications();
            if (!known) AttentionRequired?.Invoke();
        }
    }
    private async Task DecideOperationAsync(JobAttention item, AttentionAction action)
    {
        var notice = settings.PendingOperations.SingleOrDefault(x => x.Id == item.OperationId);
        if (notice is null) { MergeNotifications(); return; }
        if (notice.ToAttention().Choices.All(x => x.Action != action)) return;
        if (action == AttentionAction.ResolveNotice)
        {
            settings.PendingOperations.Remove(notice); MergeNotifications(); await SaveNoticesAsync(); return;
        }
        if (action == AttentionAction.OpenDataDirectory) { await OpenAsync(dataRoot); return; }
        if (action == AttentionAction.ReloadNiches)
        {
            await LoadNichesAsync(); return;
        }
        var area = notice.Area == OperationArea.Niches ? OperationArea.Sheets : notice.Area;
        TabIndex = area is OperationArea.Input or OperationArea.History ? 0 : 1;
        if (area == OperationArea.Input) NewJobExpanded = true;
        if (area == OperationArea.History) DetailsExpanded = true;
        FocusRequested?.Invoke(area);
    }
    private void ResolveNicheNotices()
    {
        if (settings.PendingOperations.RemoveAll(x => x.Area == OperationArea.Niches) == 0) return;
        MergeNotifications(); _ = SaveNoticesAsync();
    }
    internal async Task<object> CheckOperationNoticesAsync(Func<Task> capture)
    {
        var original = settings.PendingOperations.ToList();
        try
        {
            settings.PendingOperations.Clear();
            ReportOperation("Không mở được file thử đã chuyển.", OperationArea.History);
            ReportOperation("Không mở được file thử đã chuyển.", OperationArea.History);
            ReportOperation("Kết nối Sheets thử đã hết hạn.", OperationArea.Settings);
            await SaveNoticesAsync();
            var restored = (await store.SettingsAsync()).PendingOperations;
            if (restored.Count != 2 || restored[0].Occurrences != 2 || Attentions.Count != 2)
                throw new InvalidOperationException("Distinct operation failures must persist; repeats must deduplicate.");
            await capture();
            if (Attentions.Count != 1 || settings.PendingOperations.Count != 1)
                throw new InvalidOperationException("Resolving one operation must preserve the other.");
            await SaveNoticesAsync();
            if ((await store.SettingsAsync()).PendingOperations.Count != 1) throw new InvalidOperationException("Operation resolution did not persist.");
            var link = FullLink; var name = FullName; var threshold = ShortMinutes;
            FullLink = "https://evil.example/video/123";
            if (FullLinkError.Length == 0 || SubmitFullCommand.CanExecute(null)) throw new InvalidOperationException("Invalid link must show inline error and block submit.");
            FullLink = link; FullName = "Sai\nTên";
            if (FullNameError.Length == 0) throw new InvalidOperationException("Invalid name must have an inline explanation.");
            FullName = name; ShortMinutes = "0";
            if (!HasThresholdError) throw new InvalidOperationException("Invalid threshold must be marked at its input.");
            ShortMinutes = threshold;
            return new { distinctErrorsPersist = true, repeatedErrorsDeduplicated = true, closeKeepsRequests = true,
                resolveOnlySelected = true, inlineInvalidInputBlocked = true, modalDeferralAndReopenVerified = true, inlineTabRejectionVerified = true, diagnostic = "Actual WPF/SQLite/DPAPI fixture; no real jobs or external requests" };
        }
        finally { settings.PendingOperations = original; MergeNotifications(); await SaveNoticesAsync(); }
    }

}
