namespace TimVideoHay.Core;

public interface IGoogleConnectionService
{
    Task<SheetSelection> SelectSheetAsync(CancellationToken ct);
    Task<ChannelTarget> SelectChannelAsync(CancellationToken ct);
    Task CheckSheetAsync(SheetTarget target, CancellationToken ct);
    Task ForgetAsync(string credential, CancellationToken ct);
}

public enum ConnectionStep { Sheet, Channel, Confirm, Complete }

// Draft destinations stay separate from settings and existing jobs until the
// user confirms. Only credentials created by this draft are cleaned up on cancel.
public sealed class GoogleConnectionDraft(IGoogleConnectionService service,
    Func<SheetTarget, ChannelTarget, CancellationToken, Task> save,
    SheetTarget? existingSheet = null, ChannelTarget? existingChannel = null)
{
    private readonly HashSet<string> owned = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool sheetChecked;
    private bool closed;
    public ConnectionStep Step { get; private set; }
    public SheetTarget? Sheet { get; private set; } = existingSheet;
    public ChannelTarget? Channel { get; private set; } = existingChannel;
    public IReadOnlyList<SheetTab> Tabs { get; private set; } = existingSheet is null ? [] : [new(existingSheet.SheetId, existingSheet.TabName)];

    public async Task SelectSheetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            var selected = await service.SelectSheetAsync(ct);
            owned.Add(selected.CredentialId);
            if (selected.Tabs.Count == 0) throw new NeedsActionException("Bảng đã chọn không có tab dữ liệu.");
            var previous = Sheet?.CredentialId;
            var tab = selected.Tabs.FirstOrDefault(t => Sheet is { } prior && selected.Id == prior.SpreadsheetId && t.Id == prior.SheetId)
                ?? selected.Tabs.FirstOrDefault(t => t.Name != "Kênh nguồn") ?? selected.Tabs[0];
            var source = selected.Tabs.FirstOrDefault(t => Sheet is { } prior && selected.Id == prior.SpreadsheetId && t.Id == prior.SourceChannelSheetId)
                ?? selected.Tabs.FirstOrDefault(t => t.Name == "Kênh nguồn" && t.Id != tab.Id);
            var sameFile = Sheet?.SpreadsheetId == selected.Id;
            Sheet = new(selected.Id, tab.Id, selected.Name, tab.Name, selected.CredentialId, source?.Id, source?.Name,
                sameFile && Sheet?.SheetId == tab.Id ? Sheet.Columns : null, sameFile && Sheet?.SourceChannelSheetId == source?.Id ? Sheet?.SourceChannelColumns : null);
            Tabs = selected.Tabs; sheetChecked = false; Step = ConnectionStep.Sheet;
            if (previous != selected.CredentialId) await RetireAsync(previous);
        }
        finally { gate.Release(); }
    }

    public async Task ContinueSheetAsync(int tabId, CancellationToken ct, int? sourceTabId = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            var tab = Tabs.SingleOrDefault(t => t.Id == tabId) ?? throw new NeedsActionException("Chọn một tab trong bảng đã liên kết.");
            var previous = Sheet ?? throw new NeedsActionException("Chọn bảng trước.");
            var target = previous with { SheetId = tab.Id, TabName = tab.Name, Columns = previous.SheetId == tab.Id ? previous.Columns : null };
            if (sourceTabId is int sourceId)
            {
                var source = Tabs.SingleOrDefault(t => t.Id == sourceId) ?? throw new NeedsActionException("Chọn tab kênh nguồn trong cùng file.");
                if (source.Id == tab.Id) throw new NeedsActionException("Tab video và tab kênh nguồn phải khác nhau.");
                target = target with { SourceChannelSheetId = source.Id, SourceChannelTabName = source.Name, SourceChannelColumns = previous.SourceChannelSheetId == source.Id ? previous.SourceChannelColumns : null };
            }
            sheetChecked = false;
            await service.CheckSheetAsync(target, ct);
            ct.ThrowIfCancellationRequested();
            Sheet = target; sheetChecked = true; Step = ConnectionStep.Channel;
        }
        finally { gate.Release(); }
    }

    public async Task SelectChannelAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            if (!sheetChecked) throw new NeedsActionException("Kiểm tra bảng và tab trước khi chọn kênh.");
            var selected = await service.SelectChannelAsync(ct);
            owned.Add(selected.CredentialId);
            var previous = Channel?.CredentialId; Channel = selected;
            if (previous != selected.CredentialId) await RetireAsync(previous);
        }
        finally { gate.Release(); }
    }

    public void ContinueChannel()
    {
        RequireOpen();
        if (!sheetChecked || Step != ConnectionStep.Channel || Channel is null) throw new NeedsActionException("Chọn bảng và xác minh kênh trước.");
        Step = ConnectionStep.Confirm;
    }
    public void SetColumns(int tabId, SheetColumnMapping mapping, bool channel)
    {
        RequireOpen(); var tab = Tabs.SingleOrDefault(t => t.Id == tabId) ?? throw new NeedsActionException("Chọn tab trong file đã liên kết.");
        var target = Sheet ?? throw new NeedsActionException("Chọn bảng trước.");
        if (mapping.Columns.Length != (channel ? 5 : 4) || mapping.Columns.Distinct().Count() != mapping.Columns.Length || mapping.Columns.Any(c => c is < 0 or >= 200) || mapping.HeaderRow is < 0 or >= 30)
            throw new NeedsActionException("Chọn đầy đủ các cột khác nhau và hàng tiêu đề hợp lệ.");
        Sheet = channel ? target with { SourceChannelSheetId = tab.Id, SourceChannelTabName = tab.Name, SourceChannelColumns = mapping }
            : target with { SheetId = tab.Id, TabName = tab.Name, Columns = mapping };
        sheetChecked = false;
    }
    public void Back()
    {
        RequireOpen();
        Step = Step == ConnectionStep.Confirm ? ConnectionStep.Channel : ConnectionStep.Sheet;
    }
    public async Task CommitAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            if (Step != ConnectionStep.Confirm || !sheetChecked || Sheet is null || Channel is null)
                throw new NeedsActionException("Hoàn thành chọn bảng, tab và kênh trước khi xác nhận.");
            await save(Sheet, Channel, ct);
            owned.Clear(); closed = true; Step = ConnectionStep.Complete;
        }
        finally { gate.Release(); }
    }
    public async Task CancelAsync()
    {
        await gate.WaitAsync();
        try
        {
            foreach (var credential in owned.ToArray()) await RetireAsync(credential);
            closed = true;
        }
        finally { gate.Release(); }
    }
    private async Task RetireAsync(string? credential)
    {
        if (credential is not null && owned.Contains(credential))
        {
            await service.ForgetAsync(credential, CancellationToken.None);
            owned.Remove(credential);
        }
    }
    private void RequireOpen() { if (closed) throw new InvalidOperationException("Phiên kết nối đã kết thúc."); }
}
