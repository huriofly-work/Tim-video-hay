namespace TimVideoHay.Core;

public enum OperationArea { Settings, Input, Niches, History, General, Sheets, YouTube, GoogleClient, Storage }
public sealed record OperationNotice(string Id, string Message, OperationArea Area, DateTimeOffset Created, int Occurrences = 1, bool NeedsAuthentication = false)
{
    // Exact repeats share an entry; distinct failures remain available until explicitly resolved.
    public static OperationNotice Report(List<OperationNotice> pending, string message, OperationArea area, bool needsAuthentication = false)
    {
        var previous = pending.FindIndex(x => x.Message == message && x.Area == area);
        var item = previous < 0 ? new OperationNotice(Guid.NewGuid().ToString("N"), message, area, DateTimeOffset.UtcNow, NeedsAuthentication: needsAuthentication)
            : pending[previous] with { Occurrences = pending[previous].Occurrences + 1, NeedsAuthentication = needsAuthentication };
        if (previous < 0) pending.Add(item); else pending[previous] = item;
        return item;
    }
    public JobAttention ToAttention() => new("operation:" + Id, Area switch {
        OperationArea.Input => "Kiểm tra công việc mới", OperationArea.Niches => "Chưa tải được danh sách ngách",
        OperationArea.History => "Kiểm tra thao tác với kết quả", OperationArea.Sheets => "Kết nối Google Sheets",
        OperationArea.YouTube => "Kết nối kênh YouTube", OperationArea.GoogleClient => "Cấu hình Google", OperationArea.Storage => "Vị trí lưu file", OperationArea.General => "Kiểm tra ứng dụng", _ => "Kiểm tra kết nối và cài đặt" },
        "Thao tác chưa hoàn tất", Message, Choices()) { OperationId = Id, Results = "Công việc đang chạy giữ nguyên tiến độ.",
            Guidance = "Mở đúng nơi để khắc phục. Chỉ bỏ thông báo khi bạn đã xử lý hoặc không còn cần thao tác này.", Occurrences = Occurrences };
    private AttentionChoice[] Choices() => Area switch {
        OperationArea.General => [new(AttentionAction.OpenDataDirectory, "Mở thư mục dữ liệu app"), new(AttentionAction.OpenSettings, "Mở kết nối & cài đặt"), new(AttentionAction.ResolveNotice, "Đã xử lý, bỏ thông báo")],
        OperationArea.Niches when NeedsAuthentication => [new(AttentionAction.OpenSettings, "Kết nối lại Google Sheets"), new(AttentionAction.ResolveNotice, "Đã xử lý, bỏ thông báo")],
        OperationArea.Niches => [new(AttentionAction.ReloadNiches, "Tải lại ngách"), new(AttentionAction.OpenSettings, "Mở kết nối Sheets"), new(AttentionAction.ResolveNotice, "Đã xử lý, bỏ thông báo")],
        OperationArea.Input => [new(AttentionAction.Inspect, "Sửa công việc mới"), new(AttentionAction.ResolveNotice, "Đã xử lý, bỏ thông báo")],
        OperationArea.History => [new(AttentionAction.Inspect, "Mở lịch sử và kết quả"), new(AttentionAction.ResolveNotice, "Đã xử lý, bỏ thông báo")],
        _ => [new(AttentionAction.OpenSettings, Area switch { OperationArea.Sheets => "Mở Google Sheets", OperationArea.YouTube => "Mở kết nối YouTube", OperationArea.GoogleClient => "Mở thiết lập Google", OperationArea.Storage => "Mở nơi lưu file", _ => "Mở kết nối & cài đặt" }), new(AttentionAction.ResolveNotice, "Đã xử lý, bỏ thông báo")]
    };
}
