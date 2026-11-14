using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace TimVideoHay.Desktop;

internal sealed class UploadReviewWindow : Window
{
    private readonly RadioButton found = new() { Content = "Đã có video của công việc này trên đúng kênh", GroupName = "result" };
    private readonly RadioButton absent = new() { Content = "Tôi đã kiểm tra đúng kênh và xác nhận chưa tạo video", GroupName = "result" };
    private readonly TextBox link = new() { Margin = new Thickness(0, 6, 0, 16), ToolTip = "Link YouTube/YouTube Studio hoặc ID video gồm 11 ký tự" };
    private readonly Button confirm = new() { Content = "Xác nhận và tiếp tục", IsEnabled = false };
    internal string? VideoId => found.IsChecked == true ? ParseVideoId(link.Text) : null;
    internal bool ConfirmedAbsent => absent.IsChecked == true;
    internal static string? ParseVideoId(string text)
    {
        text = text.Trim();
        if (Regex.IsMatch(text, "^[A-Za-z0-9_-]{11}$")) return text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) return null;
        var host = uri.Host.ToLowerInvariant(); string? id = null;
        if (host == "youtu.be") id = uri.AbsolutePath.Trim('/');
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "studio.youtube.com")
        {
            var match = Regex.Match(uri.AbsolutePath, "^/(?:video|shorts)/([A-Za-z0-9_-]{11})(?:/edit)?/?$");
            if (match.Success) id = match.Groups[1].Value;
            else if (uri.AbsolutePath == "/watch")
                foreach (var part in uri.Query.TrimStart('?').Split('&')) if (part.StartsWith("v=", StringComparison.Ordinal)) id = Uri.UnescapeDataString(part[2..]);
        }
        return id is not null && Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") ? id : null;
    }
    internal UploadReviewWindow(string name)
    {
        Owner = Application.Current.Windows.OfType<AttentionWindow>().FirstOrDefault(x => x.IsVisible) ?? Application.Current.MainWindow;
        Title = "Xác nhận kết quả upload"; Width = 680; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = name, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "App chưa xác định được kết quả của phiên upload cũ. Kiểm tra trong YouTube Studio rồi chọn kết quả bên dưới. Link video sẽ được kiểm tra đúng kênh trước khi liên kết; xác nhận chưa có video sẽ cho phép tạo lại phiên upload.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) });
        body.Children.Add(found); body.Children.Add(link); body.Children.Add(absent);
        void Update() { link.IsEnabled = found.IsChecked == true; confirm.IsEnabled = absent.IsChecked == true || VideoId is not null; }
        found.Checked += (_, _) => Update(); absent.Checked += (_, _) => Update(); link.TextChanged += (_, _) => Update(); Update();
        confirm.Click += (_, _) => DialogResult = true;
        var later = new Button { Content = "Để sau", Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var actions = new WrapPanel(); actions.Children.Add(confirm); actions.Children.Add(later);
        DialogLayout.Apply(this, body, actions, 460);
        Loaded += (_, _) => WindowAttention.Present(this);
    }
}
