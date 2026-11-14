using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TimVideoHay.Core;
using TimVideoHay.Infrastructure;

namespace TimVideoHay.Desktop;

public sealed partial class MainViewModel
{
    internal async Task<object> CheckIndependentCommandsSmokeAsync()
    {
        var fixtureStore = new LocalStore(Path.Combine(dataRoot, "independent-commands"));
        var destination = new InputSmokeDestination();
        var coordinator = new JobCoordinator(fixtureStore, destination, destination, youtube);
        var fixture = new MainViewModel(fixtureStore, secrets, auth, sheets, youtube, coordinator, dataRoot);
        fixture.settings.LastActions = JobActions.Download;
        fixture.FullLink = "https://www.douyin.com/video/7659702794928147739"; fixture.FullName = "Video thử";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken settingsToken = default;
        var slowSettings = fixture.Command(async () =>
        {
            settingsToken = fixture.Ct; entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, settingsToken); }
            catch (OperationCanceledException) when (settingsToken.IsCancellationRequested) { }
            finally { finished.TrySetResult(); }
        });
        slowSettings.Execute(null); await entered.Task;
        try
        {
            if (!fixture.Busy || !fixture.SubmitFullCommand.CanExecute(null) || !fixture.RefreshHistoryCommand.CanExecute(null)
                || !fixture.OpenDataCommand.CanExecute(null) || !fixture.UserGuideCommand.CanExecute(null)
                || fixture.ConnectGoogleCommand.CanExecute(null) || !fixture.StopCommand.CanExecute(null))
                throw new InvalidOperationException("A pending settings request blocked unrelated commands or allowed a conflicting settings request.");
            // A second real command has its own cancellation token and must not replace the first one.
            fixture.RefreshHistoryCommand.Execute(null);
            fixture.CancelOperations(); await finished.Task;
            for (var i = 0; fixture.Busy && i < 100; i++) await Task.Delay(10);
            if (!settingsToken.IsCancellationRequested || fixture.Busy || !fixture.ConnectGoogleCommand.CanExecute(null))
                throw new InvalidOperationException("Independent command cancellation or busy-state cleanup failed.");
            return new { inputAvailableDuringSettings = true, historyAndOpenCommandsAvailable = true,
                conflictingSettingsBlocked = true, cancellationTokensIndependent = true,
                diagnostic = "Actual WPF commands and isolated SQLite; no external requests or user data" };
        }
        finally { fixture.CancelOperations(); await coordinator.StopAsync(); }
    }
}
