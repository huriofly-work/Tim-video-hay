using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TimVideoHay.Core;

namespace TimVideoHay.Desktop;

public sealed class SheetColumnsWindow : Window
{
    private readonly TextBox header = new() { Width = 65, Text = "1" };
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) };
    private readonly ComboBox[] selectors;
    private readonly Button save = new() { Content = "Lưu lựa chọn cột", IsEnabled = false };
    private readonly Button reload = new() { Content = "Đọc cột từ bảng" };
    private readonly CancellationTokenSource cancel;
    private readonly Func<int?, CancellationToken, Task<SheetColumnSelection>> read;
    private int loadedRow;
    private bool busy;
    public SheetColumnMapping Mapping => new(loadedRow, selectors.Select(s => ((SheetColumnOption)s.SelectedItem).Index).ToArray());
    internal bool CanSave => save.IsEnabled;
    internal void SelectForSmoke(int role, int index) => selectors[role].SelectedItem = selectors[role].Items.Cast<SheetColumnOption>().Single(c => c.Index == index);
    public SheetColumnsWindow(string tabName, bool channel, CancellationToken ct, Func<int?, CancellationToken, Task<SheetColumnSelection>> read, bool blocked = false)
    {
        this.read = read; cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Title = blocked ? "Chọn cột Kênh block" : channel ? "Chọn cột kênh nguồn" : "Chọn cột video"; Width = 640; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = tabName, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Chọn cột chứa từng thông tin. Đổi tên tiêu đề vẫn giữ lựa chọn; nếu chèn, xóa hoặc đổi thứ tự cột, hãy chọn lại.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 12) });
        var top = new WrapPanel(); top.Children.Add(new TextBlock { Text = "Hàng tiêu đề", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) }); top.Children.Add(header); top.Children.Add(reload); panel.Children.Add(top);
        var roles = blocked ? Rules.BlockedChannelHeaders : channel ? Rules.ChannelHeaders : Rules.Headers; selectors = roles.Select(_ => new ComboBox { IsEditable = false, DisplayMemberPath = "Label", IsTextSearchEnabled = true }).ToArray();
        var fields = new Grid(); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); fields.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < roles.Length; i++)
        {
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = roles[i], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) };
            Grid.SetRow(label, i); Grid.SetRow(selectors[i], i); Grid.SetColumn(selectors[i], 1);
            fields.Children.Add(label); fields.Children.Add(selectors[i]); selectors[i].SelectionChanged += (_, _) => Validate();
        }
        panel.Children.Add(fields);
        header.TextChanged += (_, _) => Validate(); panel.Children.Add(notice);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; buttons.Children.Add(new Button { Content = "Hủy", IsCancel = true }); save.Style = (Style)FindResource("Primary"); buttons.Children.Add(save);
        DialogLayout.Apply(this, panel, buttons, 600);
        reload.Click += async (_, _) => {
            if (!int.TryParse(header.Text, out var row) || row is < 1 or > 30) { notice.Text = "Nhập hàng tiêu đề từ 1 đến 30."; return; }
            await LoadAsync(row - 1);
        };
        save.Click += (_, _) => { if (save.IsEnabled) DialogResult = true; };
        Loaded += async (_, _) => await LoadAsync(null); Closed += (_, _) => cancel.Cancel();
    }
    private async Task LoadAsync(int? row)
    {
        if (busy) return; busy = true; save.IsEnabled = false; reload.IsEnabled = false; notice.Text = "Đang đọc các cột…";
        try
        {
            var data = await read(row, cancel.Token); if (cancel.IsCancellationRequested) return;
            loadedRow = data.HeaderRow; header.Text = (loadedRow + 1).ToString();
            for (var i = 0; i < selectors.Length; i++)
            {
                var previous = (selectors[i].SelectedItem as SheetColumnOption)?.Index;
                selectors[i].ItemsSource = data.Options;
                var index = previous ?? (data.Suggested?.HeaderRow == loadedRow && data.Suggested.Columns.Length == selectors.Length ? data.Suggested.Columns[i] : (int?)null);
                selectors[i].SelectedItem = data.Options.FirstOrDefault(c => c.Index == index);
            }
            notice.Text = "Chọn đủ các cột khác nhau rồi lưu. App không sửa tiêu đề hoặc cấu trúc bảng.";
        }
        catch (Exception ex) { if (!cancel.IsCancellationRequested) notice.Text = ex is TimVideoHay.Core.NeedsAuthException or TimVideoHay.Core.NeedsActionException ? ex.Message : "Chưa đọc được cột. Kiểm tra kết nối Google rồi thử lại."; }
        finally { busy = false; reload.IsEnabled = true; Validate(); }
    }
    private void Validate()
    {
        var selected = selectors.Select(s => (s.SelectedItem as SheetColumnOption)?.Index).ToArray();
        save.IsEnabled = !busy && int.TryParse(header.Text, out var row) && row == loadedRow + 1
            && selected.All(i => i is not null) && selected.Distinct().Count() == selectors.Length;
    }
}
