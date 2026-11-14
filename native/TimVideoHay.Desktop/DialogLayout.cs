using System;
using System.Windows;
using System.Windows.Controls;

namespace TimVideoHay.Desktop;

// Keep actions reachable while the content scrolls, including on smaller screens.
internal static class DialogLayout
{
    internal static void Apply(Window window, Panel body, Panel actions, double height = 620)
    {
        window.SizeToContent = SizeToContent.Manual;
        window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        window.ResizeMode = ResizeMode.CanResize;
        window.MinWidth = 520;
        window.MinHeight = 360;
        window.MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 32);
        window.Height = Math.Min(height, window.MaxHeight);
        var root = new DockPanel { Margin = new Thickness(22) };
        actions.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        body.Margin = new Thickness(0, 0, 12, 0);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        window.Content = root;
    }
}
