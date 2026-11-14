using System.ComponentModel;
using System.Windows;

namespace TimVideoHay.Desktop;

public partial class ConnectionWizardWindow : Window
{
    private readonly ConnectionWizardViewModel model;
    private bool closing, exiting, confirmed;
    public ConnectionWizardWindow(ConnectionWizardViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
        MaxHeight = System.Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32); Height = System.Math.Min(Height, MaxHeight);
        model.CancelRequested += () => Close();
        model.Completed += () => { confirmed = true; Close(); };
        model.AttentionRequested += PresentError;
        Closed += (_, _) => model.AttentionRequested -= PresentError;
        Closing += OnClosing;
    }
    private void PresentError() => WindowAttention.Present(this);
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        try { await model.CancelAsync(); exiting = true; if (confirmed) DialogResult = true; else Close(); }
        catch (System.Exception error) { closing = false; model.Error(error); }
    }
}
