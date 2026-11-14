using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimVideoHay.Desktop;
internal sealed class ActionConfirmationWindow : Window
{
    internal ActionConfirmationWindow(string title, string message, string confirm)
    {
        Title = title; Width = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
        var actions = new WrapPanel();
        var cancel = new Button { Content = "Quay lại", IsCancel = true };
        var accept = new Button { Content = confirm, Background = Brushes.LightBlue };
        cancel.Click += (_, _) => DialogResult = false; accept.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(accept);
        DialogLayout.Apply(this, body, actions, 390);
        Loaded += (_, _) => { cancel.Focus(); WindowAttention.Present(this); };
    }
    internal static bool Ask(string title, string message, string confirm, Window? owner = null)
    {
        var dialog = new ActionConfirmationWindow(title, message, confirm) { Owner = owner ?? Application.Current.MainWindow };
        return dialog.ShowDialog() == true;
    }
}
