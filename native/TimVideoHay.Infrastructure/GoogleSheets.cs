using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class GoogleSheets(GoogleTransport api, ISourceChannelReader? channelReader = null) : ISheetDestination
{
    private const string BaseUrl = "https://sheets.googleapis.com/v4/spreadsheets/";
    public async Task<SheetSelection> SelectAsync(AuthResult auth, CancellationToken ct)
    {
        var id = auth.SpreadsheetId ?? throw new ArgumentException("Picker chưa trả bảng.");
        var file = await api.JsonAsync(auth.CredentialId, HttpMethod.Get,
            "https://www.googleapis.com/drive/v3/files/" + Uri.EscapeDataString(id) + "?fields=id,name,mimeType,capabilities(canEdit)", null, ct);
        if (file["mimeType"]?.GetValue<string>() != "application/vnd.google-apps.spreadsheet" || file["capabilities"]?["canEdit"]?.GetValue<bool>() != true)
            throw new NeedsActionException("Tệp đã chọn không phải Google Sheets hoặc bạn không có quyền chỉnh sửa.");
        var metadata = await MetadataAsync(id, auth.CredentialId, ct);
        var tabs = metadata["sheets"]!.AsArray().Where(x => x!["properties"]?["sheetType"]?.GetValue<string>() == "GRID")
            .Select(x => new SheetTab(x!["properties"]!["sheetId"]!.GetValue<int>(), x["properties"]!["title"]!.GetValue<string>())).ToArray();
        if (tabs.Length == 0) throw new NeedsActionException("Bảng không có tab dữ liệu được hỗ trợ.");
        return new(id, file["name"]!.GetValue<string>(), tabs, auth.CredentialId);
    }
    private Task<JsonNode> MetadataAsync(string id, string credential, CancellationToken ct) => api.JsonAsync(credential, HttpMethod.Get,
        BaseUrl + Uri.EscapeDataString(id) + "?fields=spreadsheetId,properties(title),sheets(properties),namedRanges(name)", null, ct);
    public async Task<IReadOnlyList<SheetTab>> TabsAsync(SheetTarget target, CancellationToken ct)
    {
        var metadata = await MetadataAsync(target.SpreadsheetId, target.CredentialId, ct);
        return metadata["sheets"]!.AsArray().Where(x => x?["properties"]?["sheetType"]?.ToString() == "GRID")
            .Select(x => new SheetTab(x!["properties"]!["sheetId"]!.GetValue<int>(), x["properties"]!["title"]!.ToString())).ToArray();
    }
    public async Task<SheetColumnSelection> ColumnOptionsAsync(SheetTarget target, bool channel, int? headerRow, CancellationToken ct, bool blocked = false)
    {
        var grid = await GridAsync(target, ct, resolveDropdowns: false, rowLimit: 30);
        SheetColumnMapping? suggested = target.Columns;
        if (suggested is null)
            try { var layout = SheetLayout.Parse(grid, blocked ? Rules.BlockedChannelHeaders : channel ? Rules.ChannelHeaders : null); suggested = new(layout.HeaderRow, layout.Columns); }
            catch (NeedsActionException) { /* Custom headings are selected manually. */ }
        var row = headerRow ?? suggested?.HeaderRow ?? 0;
        if (row is < 0 or >= 30) throw new NeedsActionException("Hàng tiêu đề phải nằm trong 30 hàng đầu.");
        var cells = grid["data"]?.AsArray().FirstOrDefault()?["rowData"]?.AsArray().ElementAtOrDefault(row)?["values"]?.AsArray();
        var count = grid["properties"]!["gridProperties"]!["columnCount"]!.GetValue<int>();
        var options = Enumerable.Range(0, count).Select(i => new SheetColumnOption(i, Column(i), cells?.ElementAtOrDefault(i)?["effectiveValue"]?["stringValue"]?.ToString()
            ?? cells?.ElementAtOrDefault(i)?["userEnteredValue"]?["stringValue"]?.ToString() ?? "")).ToArray();
        return new(row, options, suggested);
    }
    private async Task<JsonNode> GridAsync(SheetTarget target, CancellationToken ct, bool resolveDropdowns = true, bool channel = false, int? rowLimit = null)
    {
        var metadata = await MetadataAsync(target.SpreadsheetId, target.CredentialId, ct);
        var tab = metadata["sheets"]!.AsArray().SingleOrDefault(x => x!["properties"]!["sheetId"]!.GetValue<int>() == target.SheetId)
            ?? throw new NeedsActionException("Tab đã bị xóa. Chọn tab mới cho công việc; đổi tên tab không gây lỗi này.");
        var properties = tab["properties"]!;
        if (properties["sheetType"]?.GetValue<string>() != "GRID") throw new NeedsActionException("Chỉ hỗ trợ tab ô dữ liệu Google Sheets.");
        var rows = properties["gridProperties"]!["rowCount"]!.GetValue<int>();
        var columns = properties["gridProperties"]!["columnCount"]!.GetValue<int>();
        if (rows > 20000 || columns > 200) throw new NeedsActionException("Tab vượt giới hạn đọc an toàn (20.000 dòng/200 cột). Dùng tab công việc nhỏ hơn; app chưa ghi dữ liệu.");
        var name = properties["title"]!.GetValue<string>();
        var range = "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'!A1:" + Column(columns - 1) + Math.Min(rows, rowLimit ?? rows);
        var fields = "sheets(properties,merges,protectedRanges,data(startRow,startColumn,rowData(values(userEnteredValue,effectiveValue,dataValidation))))";
        var json = await api.JsonAsync(target.CredentialId, HttpMethod.Get, BaseUrl + Uri.EscapeDataString(target.SpreadsheetId) +
            "?includeGridData=true&ranges=" + Uri.EscapeDataString(range) + "&fields=" + Uri.EscapeDataString(fields), null, ct);
        var grid = json["sheets"]!.AsArray().Single(x => x!["properties"]!["sheetId"]!.GetValue<int>() == target.SheetId)!;
        if (!resolveDropdowns) return grid;
        // Resolve range dropdowns once per distinct range, including the rules used by writes.
        var resolved = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var validation in SheetLayout.Parse(grid, channel ? Rules.ChannelHeaders : null, target.Columns).DropdownValidations())
        {
            if (validation["condition"]?["type"]?.GetValue<string>() != "ONE_OF_RANGE") continue;
            var reference = validation["condition"]?["values"]?[0]?["userEnteredValue"]?.GetValue<string>()?.Trim().TrimStart('=')
                ?? throw new NeedsActionException("Dropdown có vùng dữ liệu không hợp lệ.");
            if (reference.Length == 0) throw new NeedsActionException("Dropdown có vùng dữ liệu trống.");
            var namedRange = (metadata["namedRanges"]?.AsArray() ?? new JsonArray()).Any(r => r?["name"]?.GetValue<string>() == reference);
            if (!namedRange && !reference.Contains('!') && Regex.IsMatch(reference, @"^\$?[A-Za-z]+\$?\d*(?::\$?[A-Za-z]+\$?\d*)?$"))
                reference = "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'!" + reference;
            if (!resolved.TryGetValue(reference, out var allowed))
            {
                var response = await api.JsonAsync(target.CredentialId, HttpMethod.Get,
                    BaseUrl + Uri.EscapeDataString(target.SpreadsheetId) + "/values/" + Uri.EscapeDataString(reference) + "?valueRenderOption=FORMATTED_VALUE", null, ct);
                allowed = (response["values"]?.AsArray() ?? new JsonArray()).SelectMany(r => r?.AsArray() ?? new JsonArray())
                    .Select(v => v?.ToString() ?? "").Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToArray();
                if (allowed.Length > 20000) throw new NeedsActionException("Danh sách dropdown vượt 20.000 lựa chọn. Thu gọn vùng nguồn trong Sheets.");
                resolved.Add(reference, allowed);
            }
            validation["resolvedValues"] = new JsonArray(allowed.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        }
        return grid;
    }
    public async Task<IReadOnlyList<string>> NichesAsync(SheetTarget target, CancellationToken ct)
        => SheetLayout.Parse(await GridAsync(target, ct), mapping: target.Columns).NicheOptions();
    public async Task CheckBlockedChannelAsync(VideoJob job, CancellationToken ct)
    {
        var selected = job.BlockListSheet ?? throw new NeedsActionException("Chọn tab Kênh block trong Kết nối & cài đặt", JobIssueKind.Configuration);
        var secUid = !string.IsNullOrWhiteSpace(job.SourceChannel?.SecUid) ? job.SourceChannel.SecUid : job.SourceAuthorSecUid ?? "";
        if (!DouyinChannelReader.ValidSecUid(secUid))
            throw new NeedsActionException("Không lấy được kênh của đúng video Douyin.", JobIssueKind.SourceUnavailable);
        var target = BlockedTarget(selected);
        SheetLayout layout;
        try { layout = SheetLayout.Parse(await GridAsync(target, ct, resolveDropdowns: false), Rules.BlockedChannelHeaders, target.Columns); }
        catch (NeedsActionException ex) when (ex is not RetryLaterException)
        { throw new NeedsActionException("Tab Kênh block: " + ex.Message, JobIssueKind.Configuration); }
        var row = layout.FindBlockedChannel(secUid);
        if (row is not null)
            throw new NeedsActionException($"Video thuộc kênh bị block (dòng {row.Value + 1} trong tab Kênh block). App không tải, ghi bảng hay đăng video này.", JobIssueKind.BlockedChannel);
    }
    private static SheetTarget BlockedTarget(SheetTarget target)
    {
        if (target.BlockedChannelSheetId is not int id || id == target.SheetId || id == target.SourceChannelSheetId)
            throw new NeedsActionException("Chọn tab Kênh block trong Kết nối & cài đặt", JobIssueKind.Configuration);
        return target with { SheetId = id, TabName = target.BlockedChannelTabName ?? "Kênh block", Columns = target.BlockedChannelColumns };
    }
    public async Task PreflightBlockedAsync(SheetTarget target, CancellationToken ct)
    {
        var block = BlockedTarget(target);
        var layout = SheetLayout.Parse(await GridAsync(block, ct, resolveDropdowns: false), Rules.BlockedChannelHeaders, block.Columns);
        _ = layout.FindBlockedChannel("");
    }
    public async Task CheckDuplicateAsync(VideoJob job, CancellationToken ct)
    {
        var layout = SheetLayout.Parse(await GridAsync(job.Sheet!, ct, resolveDropdowns: false), mapping: job.Sheet!.Columns);
        job.Sheet = job.Sheet with { Columns = job.Sheet.Columns ?? new(layout.HeaderRow, layout.Columns) };
        var matches = layout.FindSource(job.Source!.Id);
        if (matches.Count > 0 && !job.SheetWriteStarted && !job.SheetVerified)
            throw new NeedsActionException("Đã dừng: video nguồn đã tồn tại trong bảng video (dòng " + (matches[0] + 1, JobIssueKind.Duplicate) + "). App không ghi thêm hoặc nhận dòng cũ là kết quả mới.");
    }
    public async Task<int?> FindExistingRowAsync(VideoJob job, CancellationToken ct)
    {
        var target = job.Sheet ?? throw new NeedsActionException("Công việc không có bảng gốc.");
        var source = job.Source ?? throw new NeedsActionException("Chưa xác định video nguồn.");
        var layout = SheetLayout.Parse(await GridAsync(target, ct, resolveDropdowns: false), mapping: target.Columns);
        var matches = layout.FindSource(source.Id);
        if (matches.Count > 1) throw new NeedsActionException("Bảng có nhiều dòng cùng video nguồn; chưa thể xác định kết quả.");
        return matches.Count == 0 ? null : matches[0];
    }
    public async Task DeleteSheetAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        if (job.SheetDeleted) return;
        if (!job.SheetVerified && !job.SheetWriteStarted) throw new NeedsActionException("Công việc chưa ghi bảng; không có dòng được phép xóa.");
        var target = job.Sheet ?? throw new NeedsActionException("Công việc không có bảng gốc để xóa dòng.");
        if (job.Source is null) throw new NeedsActionException("Chưa xác định video nguồn; không thể xác định dòng để xóa.");
        async Task<int?> MatchingRow()
        {
            var layout = SheetLayout.Parse(await GridAsync(target, ct, resolveDropdowns: false), mapping: target.Columns);
            var matches = layout.FindSource(job.Source.Id);
            if (matches.Count == 0) return null;
            if (matches.Count != 1) throw new NeedsActionException("Bảng có nhiều dòng cùng video nguồn. App không tự chọn dòng để xóa.");
            var row = matches[0];
            var values = new[] { job.Source.CanonicalUrl, job.Name, job.Niche, job.SheetTypeValue ?? layout.ExistingType(row, job.Type) };
            if (!layout.Exact(row, values)) throw new NeedsActionException("Dòng trên bảng đã thay đổi tên/ngách/loại hoặc công thức. Kiểm tra trước khi xóa; app chưa xóa dòng.");
            return row;
        }
        var row = await MatchingRow();
        if (row is not null)
        {
            var fresh = await MatchingRow();
            if (fresh != row) throw new NeedsActionException("Vị trí dòng vừa thay đổi. Bấm Tiếp tục để tìm lại trước khi xóa.");
            job.SheetDeleteStarted = true; await checkpoint();
            await api.JsonAsync(target.CredentialId, HttpMethod.Post, BaseUrl + Uri.EscapeDataString(target.SpreadsheetId) + ":batchUpdate",
                new { requests = new[] { new { deleteDimension = new { range = new { sheetId = target.SheetId, dimension = "ROWS", startIndex = row.Value, endIndex = row.Value + 1 } } } } }, ct);
            if (await MatchingRow() is not null) throw new NeedsActionException("Google đã nhận lệnh nhưng dòng vẫn còn. Bấm Tiếp tục để kiểm tra lại; chưa xóa mục lịch sử.");
        }
        job.SheetDeleted = true; await checkpoint();
    }
    public async Task PreflightAsync(SheetTarget target, CancellationToken ct)
    {
        _ = SheetLayout.Parse(await GridAsync(target, ct), mapping: target.Columns);
    }
    private async Task<SheetTarget> ChannelTargetAsync(SheetTarget videoTarget, SheetTarget? saved, CancellationToken ct)
    {
        var metadata = await MetadataAsync(videoTarget.SpreadsheetId, videoTarget.CredentialId, ct);
        var tabs = metadata["sheets"]!.AsArray();
        var selectedId = saved?.SheetId ?? videoTarget.SourceChannelSheetId;
        var matching = selectedId is null ? tabs.Where(t => t?["properties"]?["title"]?.ToString() == "Kênh nguồn").ToArray()
            : tabs.Where(t => t?["properties"]?["sheetId"]?.GetValue<int>() == selectedId).ToArray();
        if (matching.Length != 1 || matching[0]?["properties"]?["sheetType"]?.ToString() != "GRID")
            throw new NeedsActionException("Cần tab Kênh nguồn trong cùng file Sheets, với các cột Link kênh, Tên kênh, Đăng ký, Ngách, Dạng nội dung.");
        var p = matching[0]!["properties"]!; var id = p["sheetId"]!.GetValue<int>();
        if (id == videoTarget.SheetId) throw new NeedsActionException("Chọn tab video riêng; không chọn Kênh nguồn làm bảng video.");
        return videoTarget with { SheetId = id, TabName = p["title"]!.ToString(), Columns = saved?.Columns ?? videoTarget.SourceChannelColumns };
    }
    public async Task PrepareAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        if (channelReader is null || !job.IncludeSourceChannel) return; // Finish legacy four-cell intents without rewriting them.
        job.SourceChannelSheet = await ChannelTargetAsync(job.Sheet!, job.SourceChannelSheet, ct);
        var sourceLayout = SheetLayout.Parse(await GridAsync(job.SourceChannelSheet, ct, channel: true), Rules.ChannelHeaders, job.SourceChannelSheet.Columns);
        job.SourceChannelSheet = job.SourceChannelSheet with { Columns = job.SourceChannelSheet.Columns ?? new(sourceLayout.HeaderRow, sourceLayout.Columns) };
        if (job.SourceChannel is null) { job.SourceChannel = await channelReader.ReadAsync(job, ct); await checkpoint(); }
        if (!DouyinChannelReader.ValidSecUid(job.SourceChannel.SecUid) || job.SourceChannel.Followers < 0 || job.SourceChannel.LatestVideos.Count is < 1 or > 10)
            throw new NeedsActionException("Dữ liệu kênh nguồn chưa hợp lệ; app chưa ghi bảng.");
        _ = Rules.Text(job.SourceChannel.EnglishName, "Tên kênh tiếng Anh", 200); _ = job.SourceChannel.Classify(job.ShortMaxMilliseconds);
    }
    public async Task WriteAndVerifyAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        if (channelReader is not null && job.IncludeSourceChannel) { await WriteWithChannelAsync(job, checkpoint, ct); return; }
        var target = job.Sheet!;
        var values = new[] { job.Source!.CanonicalUrl, job.Name, job.Niche, job.Type };
        var grid = await GridAsync(target, ct);
        var layout = SheetLayout.Parse(grid, mapping: target.Columns);
        var existing = layout.FindSource(job.Source.Id);
        if (existing.Count > 1) throw new NeedsActionException("Bảng có nhiều dòng cùng video nguồn. Đối soát thủ công trước khi tiếp tục.");
        if (existing.Count == 1)
        {
            if (!job.SheetWriteStarted && !job.SheetVerified)
                throw new NeedsActionException("Đã dừng: video nguồn đã tồn tại trong bảng video. App chưa ghi thêm dòng hoặc cập nhật kênh.", JobIssueKind.Duplicate);
            var row = existing[0];
            values[3] = job.SheetTypeValue ?? layout.ExistingType(row, job.Type);
            if (!layout.Exact(row, values)) throw new NeedsActionException("Bảng đã có video này nhưng tên/ngách/loại khác. App không ghi đè dòng đang có.");
            job.SheetTypeValue = values[3]; job.SheetRow = row + 1; job.SheetVerified = true; await checkpoint(); return;
        }
        if (job.SheetVerified || job.SheetWriteStarted)
            throw new NeedsActionException("Không tìm thấy dòng đã ghi hoặc lần ghi trước chưa rõ kết quả. Kiểm tra bảng; dùng Đối soát trong lịch sử trước khi cho phép ghi lại.");
        var next = layout.LastOccupiedRow + 1;
        values[3] = job.SheetTypeValue ?? layout.TypeForDestination(next, job.Type);
        var rowCount = grid["properties"]!["gridProperties"]!["rowCount"]!.GetValue<int>();
        if (next >= rowCount) throw new NeedsActionException("Tab đã hết dòng trống. Thêm dòng trong Google Sheets rồi tiếp tục.");
        layout.ValidateDestination(next, values);
        // Re-read immediately before writing; collaborators still need to avoid concurrent edits.
        var fresh = SheetLayout.Parse(await GridAsync(target, ct), mapping: target.Columns);
        if (fresh.LastOccupiedRow != layout.LastOccupiedRow || !fresh.Columns.SequenceEqual(layout.Columns) || fresh.FindSource(job.Source.Id).Count > 0)
            throw new NeedsActionException("Bảng vừa thay đổi. Bấm Tiếp tục để đọc và xác định lại vị trí ghi.");
        fresh.ValidateDestination(next, values);
        job.SheetTypeValue = values[3]; job.SheetWriteStarted = true; job.SheetRow = next + 1; await checkpoint();
        var requests = layout.Columns.Select((col, i) => (object)new { updateCells = new {
            range = new { sheetId = target.SheetId, startRowIndex = next, endRowIndex = next + 1, startColumnIndex = col, endColumnIndex = col + 1 },
            rows = new[] { new { values = new[] { new { userEnteredValue = new { stringValue = values[i] } } } } }, fields = "userEnteredValue"
        } }).ToArray();
        await api.JsonAsync(target.CredentialId, HttpMethod.Post, BaseUrl + Uri.EscapeDataString(target.SpreadsheetId) + ":batchUpdate", new { requests }, ct);
        var after = SheetLayout.Parse(await GridAsync(target, ct), mapping: target.Columns);
        var matches = after.FindSource(job.Source.Id);
        if (matches.Count != 1 || !after.Exact(matches[0], values)) throw new NeedsActionException("Google nhận lệnh ghi nhưng chưa xác minh được đúng bốn ô. Đã dừng trước upload; kiểm tra bảng rồi Tiếp tục.");
        job.SheetVerified = true; job.SheetRow = matches[0] + 1; await checkpoint();
    }
    private async Task WriteWithChannelAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        await CheckDuplicateAsync(job, ct);
        await PrepareAsync(job, checkpoint, ct);
        var target = job.Sheet!; var channelTarget = job.SourceChannelSheet!; var channel = job.SourceChannel!;
        var values = new[] { job.Source!.CanonicalUrl, job.Name, job.Niche, job.Type };
        var channelValues = new[] { channel.Url, channel.EnglishName, channel.Followers.ToString(System.Globalization.CultureInfo.InvariantCulture), job.Niche, channel.Classify(job.ShortMaxMilliseconds) };
        var grid = await GridAsync(target, ct); var layout = SheetLayout.Parse(grid, mapping: target.Columns);
        var channelGrid = await GridAsync(channelTarget, ct, channel: true); var channels = SheetLayout.Parse(channelGrid, Rules.ChannelHeaders, channelTarget.Columns);
        var existing = layout.FindSource(job.Source.Id); var channelRows = channels.FindChannel(channel.SecUid);
        if (existing.Count > 1 || channelRows.Count > 1) throw new NeedsActionException("Có nhiều dòng trùng video/kênh nguồn. App đã dừng, không tự chọn dòng để cập nhật.");
        if (existing.Count == 1)
        {
            if (!job.SheetWriteStarted && !job.SheetVerified) throw new NeedsActionException("Đã dừng: video đã tồn tại trong bảng video. Chưa cập nhật kênh hoặc thêm dòng.", JobIssueKind.Duplicate);
            values[3] = job.SheetTypeValue ?? layout.ExistingType(existing[0], job.Type);
            if (!layout.Exact(existing[0], values)) throw new NeedsActionException("Dòng video của lượt ghi trước đã bị thay đổi. Kiểm tra bảng trước khi tiếp tục.");
            if (channelRows.Count != 1 || !channels.Exact(channelRows[0], channelValues))
                throw new NeedsActionException("Đã tìm thấy dòng video của lượt ghi trước nhưng dòng kênh chưa khớp checkpoint. Kiểm tra Kênh nguồn; app không ghi lại hoặc thêm dòng trùng.");
            job.SheetRow = existing[0] + 1; job.SheetVerified = true; await checkpoint(); return;
        }
        if (job.SheetWriteStarted || job.SheetVerified) throw new NeedsActionException("Chưa tìm thấy kết quả của lần ghi trước. Đối soát bảng trước khi cho phép ghi lại; không tự thêm dòng.");
        var next = layout.LastOccupiedRow + 1; var channelRow = channelRows.Count == 1 ? channelRows[0] : channels.LastOccupiedRow + 1;
        var updating = channelRows.Count == 1;
        if (next >= grid["properties"]!["gridProperties"]!["rowCount"]!.GetValue<int>() || channelRow >= channelGrid["properties"]!["gridProperties"]!["rowCount"]!.GetValue<int>())
            throw new NeedsActionException("Tab video hoặc Kênh nguồn đã hết dòng trống. Thêm dòng trên Sheets rồi tiếp tục.");
        values[3] = job.SheetTypeValue ?? layout.TypeForDestination(next, job.Type);
        layout.ValidateDestination(next, values); channels.ValidateDestination(channelRow, channelValues, overwrite: updating);
        var beforeChannel = updating ? channels.Values(channelRow) : null;
        var fresh = SheetLayout.Parse(await GridAsync(target, ct), mapping: target.Columns);
        var freshChannels = SheetLayout.Parse(await GridAsync(channelTarget, ct, channel: true), Rules.ChannelHeaders, channelTarget.Columns);
        if (fresh.FindSource(job.Source.Id).Count > 0) throw new NeedsActionException("Đã dừng: video vừa được thêm vào bảng. Không ghi trùng hoặc cập nhật kênh.", JobIssueKind.Duplicate);
        var freshRows = freshChannels.FindChannel(channel.SecUid);
        if (fresh.LastOccupiedRow != layout.LastOccupiedRow || !fresh.Columns.SequenceEqual(layout.Columns)
            || !freshChannels.Columns.SequenceEqual(channels.Columns) || !freshRows.SequenceEqual(channelRows)
            || updating && !freshChannels.Values(channelRow).SequenceEqual(beforeChannel!)
            || !updating && freshChannels.LastOccupiedRow != channels.LastOccupiedRow)
            throw new NeedsActionException("Bảng vừa thay đổi vị trí/dữ liệu kênh. Bấm Tiếp tục để đọc lại; app chưa ghi.");
        fresh.ValidateDestination(next, values); freshChannels.ValidateDestination(channelRow, channelValues, overwrite: updating);
        job.SheetTypeValue = values[3]; job.SheetRow = next + 1; job.SourceChannelUpdated = updating; job.SheetWriteStarted = true; await checkpoint();
        var requests = Cells(target.SheetId, next, fresh.Columns, values).Concat(Cells(channelTarget.SheetId, channelRow, freshChannels.Columns, channelValues, numericColumn: 2)).ToArray();
        await api.JsonAsync(target.CredentialId, HttpMethod.Post, BaseUrl + Uri.EscapeDataString(target.SpreadsheetId) + ":batchUpdate", new { requests }, ct);
        var after = SheetLayout.Parse(await GridAsync(target, ct), mapping: target.Columns); var afterChannels = SheetLayout.Parse(await GridAsync(channelTarget, ct, channel: true), Rules.ChannelHeaders, channelTarget.Columns);
        var matches = after.FindSource(job.Source.Id); var channelMatches = afterChannels.FindChannel(channel.SecUid);
        if (matches.Count != 1 || !after.Exact(matches[0], values) || channelMatches.Count != 1 || !afterChannels.Exact(channelMatches[0], channelValues))
            throw new NeedsActionException("Google đã nhận lệnh nhưng chưa xác minh được cả dòng video và kênh. Giữ checkpoint để thử lại; không ghi/upload trùng.");
        job.SheetVerified = true; job.SheetRow = matches[0] + 1; job.Message = job.SourceChannelMessage; await checkpoint();
    }
    private static IEnumerable<object> Cells(int sheetId, int row, int[] columns, string[] values, int numericColumn = -1)
        => columns.Select((col, i) => (object)new { updateCells = new {
            range = new { sheetId, startRowIndex = row, endRowIndex = row + 1, startColumnIndex = col, endColumnIndex = col + 1 },
            rows = new[] { new { values = new[] { new { userEnteredValue = i == numericColumn
                ? (object)new { numberValue = long.Parse(values[i], System.Globalization.CultureInfo.InvariantCulture) } : new { stringValue = values[i] } } } } }, fields = "userEnteredValue"
        } });
    private static string Column(int index) { var name = ""; for (var x = index + 1; x > 0; x = (x - 1) / 26) name = (char)('A' + (x - 1) % 26) + name; return name; }
}

// Reads user-entered values separately from effective values: formulas yielding "" still occupy a row.
public sealed class SheetLayout
{
    private readonly JsonNode grid;
    private readonly JsonArray rows;
    private readonly string[] headers;
    public int HeaderRow { get; }
    public int[] Columns { get; }
    public int LastOccupiedRow { get; }
    private SheetLayout(JsonNode grid, JsonArray rows, int header, int[] columns, string[] headers)
    {
        this.grid = grid; this.rows = rows; HeaderRow = header; Columns = columns; this.headers = headers;
        LastOccupiedRow = Math.Max(header, Enumerable.Range(0, rows.Count).Where(r => Cells(r).Any(c => c?["userEnteredValue"] is not null)).DefaultIfEmpty(header).Max());
    }
    public static SheetLayout Parse(JsonNode grid, string[]? headers = null, SheetColumnMapping? mapping = null)
    {
        headers ??= Rules.Headers;
        var data = grid["data"]?.AsArray();
        if (data is { Count: > 1 } || data?[0]?["startRow"]?.GetValue<int>() is > 0 || data?[0]?["startColumn"]?.GetValue<int>() is > 0)
            throw new NeedsActionException("Vùng dữ liệu Google Sheets không hợp lệ.");
        var rows = data is { Count: > 0 } ? data[0]?["rowData"]?.AsArray() ?? new JsonArray() : new JsonArray();
        if (mapping is not null)
        {
            var count = grid["properties"]?["gridProperties"]?["columnCount"]?.GetValue<int>() ?? rows.Select(r => r?["values"]?.AsArray().Count ?? 0).DefaultIfEmpty(0).Max();
            if (mapping.HeaderRow is < 0 or >= 30 || mapping.Columns.Length != headers.Length || mapping.Columns.Distinct().Count() != headers.Length
                || mapping.Columns.Any(c => c < 0 || c >= count))
                throw new NeedsActionException("Cấu hình cột không còn hợp lệ. Chọn lại các cột khác nhau, nằm trong tab đã chọn.");
            return new(grid, rows, mapping.HeaderRow, mapping.Columns.ToArray(), headers);
        }
        var candidates = new List<(int Row, int[] Columns)>();
        for (var r = 0; r < Math.Min(rows.Count, 30); r++)
        {
            var cells = rows[r]?["values"]?.AsArray() ?? new JsonArray();
            var matches = headers.Select(h => Enumerable.Range(0, cells.Count).Where(c => CellText(cells[c]).Trim().Normalize() == h).ToArray()).ToArray();
            if (matches.All(m => m.Length == 1)) candidates.Add((r, matches.Select(m => m[0]).ToArray()));
            else if (matches.All(m => m.Length >= 1)) throw new NeedsActionException("Tiêu đề cột bị trùng. Cần đúng một cột cho mỗi tên bắt buộc.");
        }
        if (candidates.Count != 1) throw new NeedsActionException("Cần một hàng tiêu đề trong 30 dòng đầu: " + string.Join(", ", headers) + ". Không tự đổi cấu trúc bảng.");
        return new(grid, rows, candidates[0].Row, candidates[0].Columns, headers);
    }
    private JsonArray Cells(int row) => row < rows.Count ? rows[row]?["values"]?.AsArray() ?? new JsonArray() : new JsonArray();
    private JsonNode? Cell(int row, int col) { var cells = Cells(row); return col < cells.Count ? cells[col] : null; }
    private static string CellText(JsonNode? cell) => cell?["effectiveValue"]?["stringValue"]?.GetValue<string>() ?? cell?["userEnteredValue"]?["stringValue"]?.GetValue<string>()
        ?? cell?["effectiveValue"]?["numberValue"]?.ToString() ?? cell?["userEnteredValue"]?["numberValue"]?.ToString() ?? "";
    // Block-list cells must not hide bool, error or formula content as empty: a broken link would silently unblock a channel.
    private static string BlockCellText(JsonNode? cell) => CellText(cell) is { Length: > 0 } text ? text
        : cell?["effectiveValue"]?["boolValue"]?.ToString() ?? cell?["userEnteredValue"]?["boolValue"]?.ToString()
        ?? cell?["effectiveValue"]?["errorValue"]?.ToJsonString() ?? cell?["userEnteredValue"]?["formulaValue"]?.ToString() ?? "";
    public string[] Values(int row) => Columns.Select(c => CellText(Cell(row, c))).ToArray();
    public int? FindBlockedChannel(string secUid)
    {
        for (var row = HeaderRow + 1; row < rows.Count; row++)
        {
            var text = BlockCellText(Cell(row, Columns[0]));
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host is not ("www.douyin.com" or "douyin.com")
                || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || !uri.AbsolutePath.StartsWith("/user/", StringComparison.Ordinal)
                || !DouyinChannelReader.ValidSecUid(uri.AbsolutePath[6..].TrimEnd('/')))
                throw new NeedsActionException($"Link kênh không hợp lệ ở dòng {row + 1} trong tab Kênh block. Sửa link kênh Douyin rồi Tiếp tục.", JobIssueKind.InvalidData);
        }
        var matches = FindChannel(secUid);
        return matches.Count == 0 ? null : matches[0];
    }
    public List<int> FindChannel(string secUid) => Enumerable.Range(HeaderRow + 1, Math.Max(0, rows.Count - HeaderRow - 1))
        .Where(row => Uri.TryCreate(CellText(Cell(row, Columns[0])), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host is "www.douyin.com" or "douyin.com"
            && uri.AbsolutePath.TrimEnd('/') == "/user/" + secUid && uri.UserInfo.Length == 0 && uri.IsDefaultPort).ToList();
    public IEnumerable<JsonNode> DropdownValidations()
        => Enumerable.Range(HeaderRow + 1, Math.Max(0, rows.Count - HeaderRow - 1))
            .SelectMany(r => Columns.Select(c => Cell(r, c)?["dataValidation"]))
            .Where(v => v is not null).Select(v => v!);
    public IReadOnlyList<string> NicheOptions()
    {
        // Prefer the rule for the next write. Older rows may have outdated dropdowns.
        var validation = Cell(LastOccupiedRow + 1, Columns[2])?["dataValidation"]
            ?? Enumerable.Range(HeaderRow + 1, Math.Max(0, rows.Count - HeaderRow - 1))
                .Select(r => Cell(r, Columns[2])?["dataValidation"]).FirstOrDefault(v => v is not null);
        if (validation is null) throw new NeedsActionException("Cột Ngách chưa có dropdown. Tạo danh sách thả xuống trong Google Sheets rồi bấm Làm mới ngách.");
        var options = AllowedValues(validation).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToArray();
        if (options.Length == 0) throw new NeedsActionException("Dropdown Ngách đang trống. Bổ sung lựa chọn trong Google Sheets rồi bấm Làm mới ngách.");
        return options;
    }
    private static string[] AllowedValues(JsonNode validation) => validation["condition"]?["type"]?.GetValue<string>() switch
    {
        "ONE_OF_LIST" => validation["condition"]?["values"]?.AsArray().Select(v => v?["userEnteredValue"]?.GetValue<string>() ?? "").ToArray() ?? [],
        "ONE_OF_RANGE" when validation["resolvedValues"] is JsonArray values => values.Select(v => v?.GetValue<string>() ?? "").ToArray(),
        _ => throw new NeedsActionException("Dropdown có quy tắc chưa được hỗ trợ. Dùng danh sách nhập trực tiếp hoặc danh sách từ vùng ô trong Sheets.")
    };
    public bool Exact(int row, string[] values) => Columns.Select((c, i) => CellText(Cell(row, c)) == values[i] && Cell(row, c)?["userEnteredValue"]?["formulaValue"] is null).All(x => x);
    public List<int> FindSource(string sourceId)
    {
        var result = new List<int>();
        for (var row = HeaderRow + 1; row < rows.Count; row++)
        {
            var value = CellText(Cell(row, Columns[0]));
            try { if (Rules.SourceId(Rules.DouyinUri(value)) == sourceId) result.Add(row); } catch (ArgumentException) { }
        }
        return result;
    }
    public string ExistingType(int row, string logicalType)
    {
        var actual = CellText(Cell(row, Columns[3]));
        if (TypeAliases(logicalType).Contains(actual)) return actual;
        throw new NeedsActionException("Dòng đã có video nguồn nhưng phân loại không khớp thời lượng. Kiểm tra bảng, không ghi đè.");
    }
    public string TypeForDestination(int row, string logicalType)
    {
        var validation = Cell(row, Columns[3])?["dataValidation"];
        if (validation?["strict"]?.GetValue<bool>() == true && validation["condition"]?["type"]?.GetValue<string>() is "ONE_OF_LIST" or "ONE_OF_RANGE")
        {
            var allowed = AllowedValues(validation);
            if (allowed.Contains(logicalType)) return logicalType;
            var matches = TypeAliases(logicalType).Where(allowed.Contains).ToArray();
            if (matches.Length == 1) return matches[0];
            throw new NeedsActionException("Dropdown Loại video không có đúng một nhãn phù hợp Short/Long. Kiểm tra quy tắc trong Sheets.");
        }
        var existing = Enumerable.Range(HeaderRow + 1, Math.Max(0, rows.Count - HeaderRow - 1)).Select(r => CellText(Cell(r, Columns[3]))).ToArray();
        if (existing.Any(v => v is "Video ngắn" or "Video dài") && !existing.Any(v => v is "Short" or "Shorts" or "Long"))
            return logicalType == "Short" ? "Video ngắn" : "Video dài";
        if (logicalType == "Short" && existing.Contains("Shorts") && !existing.Contains("Short")) return "Shorts";
        return logicalType;
    }
    private static string[] TypeAliases(string logicalType) => logicalType == "Short" ? ["Short", "Shorts", "Video ngắn"] : ["Long", "Video dài"];
    public void ValidateDestination(int row, string[] values, bool overwrite = false)
    {
        for (var i = 0; i < Columns.Length; i++)
        {
            var col = Columns[i];
            if (Cell(row, col)?["userEnteredValue"] is not null && (!overwrite || Cell(row, col)?["userEnteredValue"]?["formulaValue"] is not null)) throw new NeedsActionException("Ô đích đã có dữ liệu hoặc công thức không được phép ghi đè.");
            foreach (var merge in grid["merges"]?.AsArray() ?? new JsonArray())
                if (Contains(merge!, row, col)) throw new NeedsActionException("Ô đích nằm trong vùng gộp. Chọn tab không gộp vùng dữ liệu.");
            foreach (var protection in grid["protectedRanges"]?.AsArray() ?? new JsonArray())
                if (protection?["warningOnly"]?.GetValue<bool>() != true && protection?["range"] is JsonNode range && Contains(range, row, col))
                    throw new NeedsActionException("Ô đích có bảo vệ. Bỏ bảo vệ vùng dữ liệu hoặc chọn tab khác.");
            var validation = Cell(row, col)?["dataValidation"];
            if (validation?["strict"]?.GetValue<bool>() != true) continue;
            var allowed = AllowedValues(validation);
            if (!allowed.Contains(values[i])) throw new NeedsActionException($"Giá trị '{values[i]}' chưa có trong danh sách cho cột {headers[i]}. Bổ sung lựa chọn trên bảng rồi Tiếp tục.");
        }
    }
    private static bool Contains(JsonNode range, int row, int column) =>
        row >= (range["startRowIndex"]?.GetValue<int>() ?? 0) && row < (range["endRowIndex"]?.GetValue<int>() ?? int.MaxValue) &&
        column >= (range["startColumnIndex"]?.GetValue<int>() ?? 0) && column < (range["endColumnIndex"]?.GetValue<int>() ?? int.MaxValue);
}
