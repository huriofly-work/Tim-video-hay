using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TimVideoHay.Core;
using TimVideoHay.Infrastructure;

namespace TimVideoHay.Desktop;

public sealed partial class MainViewModel
{
    internal async Task<object> CheckRepeatedNicheSmokeAsync(Func<MainViewModel, Task> exercise)
    {
        var root = Path.Combine(dataRoot, "repeated-niche");
        var fixtureStore = new LocalStore(root);
        var destination = new InputSmokeDestination();
        var coordinator = new JobCoordinator(fixtureStore, destination, destination, youtube);
        var fixture = new MainViewModel(fixtureStore, secrets, auth, sheets, youtube, coordinator, root);
        // No initialization: the fixture has no credentials and must never read Google.
        fixture.settings.LastActions = JobActions.WriteSheet;
        fixture.settings.Sheet = new("input-fixture", 7, "Bảng thử", "Video", "unused");
        fixture.Niches.Add("Vũ trụ"); fixture.Niches.Add("Động vật");
        try
        {
            await exercise(fixture);
            var jobs = await fixtureStore.ListAsync();
            if (jobs.Count != 3 || jobs.Any(j => j.Niche != "Động vật" || j.Sheet != fixture.settings.Sheet))
                throw new InvalidOperationException("Repeated submissions did not keep the selected niche and sheet.");
            return new { consecutiveSubmissions = jobs.Count, sameNicheRetained = true, submitButtonEnabledWithoutReselecting = true,
                savedJobNichesVerified = true, diagnostic = "Actual WPF bindings, submit command and SQLite; mock source and sheet, no external calls" };
        }
        finally { await fixture.ShutdownAsync(); }
    }

    private sealed class InputSmokeDestination : IVideoSource, ISheetDestination
    {
        public Task<SourceVideo> ResolveAsync(string input, CancellationToken ct)
            => Task.FromResult(new SourceVideo(Rules.SourceId(Rules.DouyinUri(input))!, input));
        // Hold the worker locally while the actual submit command queues subsequent videos.
        public Task PreflightAsync(SheetTarget target, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);
        public Task<SourceMetadata> ReadMetadataAsync(SourceVideo source, CancellationToken ct) => throw new InvalidOperationException("Unexpected metadata read.");
        public Task<MediaFile> DownloadAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct) => throw new InvalidOperationException("Unexpected download.");
        public Task ValidateFileAsync(MediaFile file, CancellationToken ct) => throw new InvalidOperationException("Unexpected file access.");
        public Task ValidateFolderDeletionAsync(VideoJob job, CancellationToken ct) => throw new InvalidOperationException("Unexpected deletion.");
        public Task DeleteFolderAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) => throw new InvalidOperationException("Unexpected deletion.");
        public Task WriteAndVerifyAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) => throw new InvalidOperationException("Unexpected sheet write.");
        public Task DeleteSheetAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) => throw new InvalidOperationException("Unexpected sheet deletion.");
    }
}
