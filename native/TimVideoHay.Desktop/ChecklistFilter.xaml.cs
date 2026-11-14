using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimVideoHay.Desktop;

public partial class ChecklistFilter : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(ChecklistFilter));
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(nameof(Summary), typeof(string), typeof(ChecklistFilter));
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(ChecklistFilter));
    public static readonly DependencyProperty ClearLabelProperty = DependencyProperty.Register(nameof(ClearLabel), typeof(string), typeof(ChecklistFilter));
    public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.Register(nameof(ClearCommand), typeof(ICommand), typeof(ChecklistFilter));
    public IEnumerable ItemsSource { get => (IEnumerable)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string Summary { get => (string)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public string ClearLabel { get => (string)GetValue(ClearLabelProperty); set => SetValue(ClearLabelProperty, value); }
    public ICommand ClearCommand { get => (ICommand)GetValue(ClearCommandProperty); set => SetValue(ClearCommandProperty, value); }
    public ChecklistFilter() => InitializeComponent();
    internal ItemsControl ChoicesForSmoke => ChoicesList;
    internal Button ClearForSmoke => ClearFilter;
    internal void OpenForSmoke(bool open) { FilterToggle.IsChecked = open; ChoicesList.UpdateLayout(); }
}
