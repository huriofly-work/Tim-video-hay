using System;
using System.Windows;
using System.Windows.Controls;
using TimVideoHay.Core;

namespace TimVideoHay.Desktop;

// Count visible Unicode characters, so Vietnamese accents and emoji are not split.
public static class VideoNameInput
{
    public static readonly DependencyProperty LimitedProperty = DependencyProperty.RegisterAttached(
        "Limited", typeof(bool), typeof(VideoNameInput), new PropertyMetadata(false, Changed));
    public static bool GetLimited(DependencyObject element) => (bool)element.GetValue(LimitedProperty);
    public static void SetLimited(DependencyObject element, bool value) => element.SetValue(LimitedProperty, value);
    private static void Changed(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box) return;
        if ((bool)e.NewValue) { box.TextChanged += Limit; Limit(box, null); }
        else box.TextChanged -= Limit;
    }
    private static void Limit(object sender, TextChangedEventArgs? e)
    {
        var box = (TextBox)sender;
        var limited = Rules.LimitNameInput(box.Text);
        if (limited == box.Text) return;
        var caret = box.SelectionStart;
        box.Text = limited;
        box.SelectionStart = Math.Min(caret, limited.Length);
    }
}
