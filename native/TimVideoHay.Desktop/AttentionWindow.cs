using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimVideoHay.Core;

namespace TimVideoHay.Desktop;

internal sealed class AttentionWindow : Window
{
    private readonly MainViewModel model;
    private readonly ListBox jobs = new() { DisplayMemberPath = nameof(JobAttention.Name), MaxHeight = 110, MinHeight = 44 };
    private readonly TextBlock heading = new() { FontSize = 22, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock stage = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 14 };
    private readonly TextBlock checkpoint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock guidance = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.DarkSlateBlue };
    private readonly TextBlock category = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) };
    private readonly WrapPanel actions = new();
    private readonly List<Button> decisionButtons = new();
    private string? selectedId;
    internal JobAttention? SelectedAttention => jobs.SelectedItem as JobAttention;
    internal int DecisionButtonCount => decisionButtons.Count;
    internal void SelectJob(string id) => jobs.SelectedItem = model.Attentions.Single(x => x.JobId == id || x.RelatedJobIds.Contains(id));
    internal void ChooseForSmoke(AttentionAction action)
    {
        var item = SelectedAttention ?? throw new InvalidOperationException("No notification selected.");
        var button = decisionButtons.Single(x => Equals(x.Tag, action));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    internal AttentionWindow(MainViewModel model)
    {
        this.model = model; Owner = Application.Current.MainWindow;
        Title = "Cần bạn lựa chọn — Tìm video hay"; Width = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel(); body.Children.Add(heading);
        body.Children.Add(new TextBlock { Text = "Tiến độ được giữ nguyên. Các công việc khác tiếp tục chạy.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        body.Children.Add(jobs); body.Children.Add(stage); body.Children.Add(category); body.Children.Add(message); body.Children.Add(guidance); body.Children.Add(checkpoint);
        DialogLayout.Apply(this, body, actions, 470);
        jobs.ItemsSource = model.Attentions;
        jobs.SelectionChanged += (_, _) => { if (SelectedAttention is { } current) selectedId = current.JobId; Render(); };
        model.Attentions.CollectionChanged += OnItemsChanged;
        model.PropertyChanged += OnModelChanged;
        Closed += (_, _) => { model.Attentions.CollectionChanged -= OnItemsChanged; model.PropertyChanged -= OnModelChanged; };
        Synchronize();
    }
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Synchronize();
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Busy)) foreach (var button in decisionButtons) button.IsEnabled = !model.Busy;
    }
    private void Synchronize()
    {
        heading.Text = $"Cần bạn lựa chọn · {model.Attentions.Count} yêu cầu";
        var selected = model.Attentions.FirstOrDefault(x => x.JobId == selectedId);
        if (selected is not null) jobs.SelectedItem = selected;
        else if (jobs.SelectedItem is null && model.Attentions.Count > 0) jobs.SelectedIndex = 0;
        Render();
    }
    private void Render()
    {
        actions.Children.Clear(); decisionButtons.Clear();
        if (SelectedAttention is { } item)
        {
            category.Text = item.OperationId is null ? "Cần bạn quyết định" : "Thao tác cần khắc phục";
            category.Foreground = item.OperationId is null ? Brushes.DarkGoldenrod : Brushes.Firebrick;
            guidance.Text = item.Guidance;
            message.Foreground = item.OperationId is null ? Brushes.DarkSlateGray : Brushes.Firebrick;
            stage.Text = item.Name + " · " + item.Stage; message.Text = item.Message; checkpoint.Text = item.OperationId is null ? $"Tiến độ đã lưu: {item.Progress:0.#}% · {item.Results}" : item.Results + (item.Occurrences > 1 ? $" · Đã gặp {item.Occurrences} lần" : "");
            foreach (var choice in item.Choices)
            {
                if (item.RelatedJobIds.Count > 1 && choice.Action == AttentionAction.OmitAffected) continue;
                var label = item.RelatedJobIds.Count > 1 ? choice.Action switch {
                    AttentionAction.Pause => "Tạm dừng nhóm công việc",
                    AttentionAction.Retry => "Chờ và tự thử lại nhóm",
                    AttentionAction.ReconnectGoogle => "Đăng nhập lại và tiếp tục nhóm", _ => choice.Label } : choice.Label;
                var button = new Button { Content = label, IsEnabled = !model.Busy, Margin = new Thickness(0, 3, 8, 3), MaxWidth = 320, Tag = choice.Action };
                if (decisionButtons.Count == 0) button.Background = Brushes.LightBlue;
                button.Click += (_, _) => model.DecideAttention(item, choice.Action);
                actions.Children.Add(button); decisionButtons.Add(button);
            }
        }
        else { category.Text = ""; guidance.Text = ""; checkpoint.Text = ""; stage.Text = "Không còn công việc chờ lựa chọn."; message.Text = "Các lựa chọn đã được xử lý. Bạn có thể đóng cửa sổ này."; }
        var later = new Button { Content = SelectedAttention is null ? "Đóng" : "Để sau", Margin = new Thickness(0, 3, 0, 3), ToolTip = "Đóng thông báo; giữ tiến độ và yêu cầu chờ, không tạm dừng các công việc khác." };
        later.Click += (_, _) => Close(); actions.Children.Add(later);
    }
}
