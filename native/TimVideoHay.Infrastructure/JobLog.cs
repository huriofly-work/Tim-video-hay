using System.Text;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class JobLog(string root)
{
    private readonly object gate = new();
    private readonly Dictionary<string, string> previous = new();
    public void Record(VideoJob job)
    {
        lock (gate)
        {
            if (!Guid.TryParse(job.Id, out _)) return;
            var state = job.State + "/" + job.SelectedActions;
            if (previous.TryGetValue(job.Id, out var last) && last == state) return;
            previous[job.Id] = state;
            try
            {
                PrivateFiles.EnsureDirectory(root); var path = Path.Combine(root, "jobs.log");
                PrivateFiles.RejectLinks(root, recursive: true);
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                {
                    for (var i = 3; i >= 1; i--) { var older = path + "." + i; if (File.Exists(older)) File.Move(older, path + "." + (i + 1), true); }
                    File.Move(path, path + ".1", true);
                }
                // Enum state/mode + generated ID only: never names, stages from old
                // checkpoints, cookies, tokens, HTTP bodies or session URI.
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {job.Id} {state}\n", Encoding.UTF8);
            }
            catch (IOException) { /* Diagnostics must not change the outcome of an external mutation. */ }
            catch (UnauthorizedAccessException) { }
            catch (NeedsActionException) { }
        }
    }
}
