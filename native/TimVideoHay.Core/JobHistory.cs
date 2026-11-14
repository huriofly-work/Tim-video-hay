namespace TimVideoHay.Core;

public enum HistoryState { All, Unfinished, Running, Queued, Waiting, Attention, Paused, Complete }
public enum HistoryOrder { Workflow, Newest, Oldest }

public static class JobQueue
{
    // Shared by the worker and the displayed queue; old jobs fall back to creation time.
    public static IOrderedEnumerable<VideoJob> Order(IEnumerable<VideoJob> jobs) => jobs
        .Where(j => !j.RemovedFromHistory && j.State == JobState.Queued)
        .OrderBy(j => j.DeletionRequested ? 0 : 1).ThenBy(j => j.EnqueuedAt ?? j.Created)
        .ThenBy(j => j.Created).ThenBy(j => j.Id, StringComparer.Ordinal);
}

public static class JobHistory
{
    public static VideoJob[] Project(IEnumerable<VideoJob> source, JobActions action = JobActions.None,
        HistoryState state = HistoryState.All, HistoryOrder order = HistoryOrder.Workflow, string search = "")
        => ProjectSelected(source, action, [state], order, search, requireAllActions: true);

    // OR within each group; AND between groups. No checked choices means all.
    public static VideoJob[] ProjectSelected(IEnumerable<VideoJob> source, JobActions actions,
        IReadOnlyCollection<HistoryState> states, HistoryOrder order = HistoryOrder.Workflow,
        string search = "", bool requireAllActions = false, bool includeEmptyActions = false)
    {
        var jobs = source.Where(j => !j.RemovedFromHistory).ToArray();
        foreach (var job in jobs) job.QueuePosition = 0;
        var position = 0;
        foreach (var job in JobQueue.Order(jobs)) job.QueuePosition = ++position;
        var terms = search.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = jobs.Where(j => ((includeEmptyActions && j.CurrentActions == JobActions.None)
            || (actions == JobActions.None ? !includeEmptyActions : requireAllActions ? j.CurrentActions.HasFlag(actions) : (j.CurrentActions & actions) != 0))
            && (states.Count == 0 || states.Any(state => MatchesState(j, state)))
            && terms.All(t => Contains(j.Name, t) || Contains(j.Niche, t) || Contains(j.Input, t) || Contains(j.Source?.CanonicalUrl, t) || Contains(j.Source?.Id, t)));
        return order switch
        {
            HistoryOrder.Newest => filtered.OrderByDescending(j => j.Created).ThenBy(j => j.Id, StringComparer.Ordinal).ToArray(),
            HistoryOrder.Oldest => filtered.OrderBy(j => j.Created).ThenBy(j => j.Id, StringComparer.Ordinal).ToArray(),
            _ => filtered.OrderBy(Group).ThenBy(j => j.QueuePosition)
                .ThenBy(j => j.State == JobState.WaitingForYouTube ? j.NextYouTubeCheckAt ?? DateTimeOffset.MinValue : DateTimeOffset.MinValue)
                .ThenByDescending(j => j.Created).ThenBy(j => j.Id, StringComparer.Ordinal).ToArray()
        };
    }
    public static bool NeedsAttention(VideoJob job) => job.State is JobState.NeedsAuth or JobState.NeedsAction or JobState.Failed;
    private static bool MatchesState(VideoJob job, HistoryState state) => state switch
    {
        HistoryState.Unfinished => job.State != JobState.Complete,
        HistoryState.Running => job.State == JobState.Running,
        HistoryState.Queued => job.State == JobState.Queued,
        HistoryState.Waiting => job.State is JobState.WaitingForYouTube or JobState.WaitingToRetry,
        HistoryState.Attention => NeedsAttention(job),
        HistoryState.Paused => job.State == JobState.Paused,
        HistoryState.Complete => job.State == JobState.Complete,
        _ => true
    };
    private static int Group(VideoJob job) => job.State switch
    {
        JobState.Running => 0, JobState.Queued => 1, JobState.WaitingForYouTube or JobState.WaitingToRetry => 2,
        JobState.NeedsAuth or JobState.NeedsAction or JobState.Failed => 3, JobState.Paused => 4, _ => 5
    };
    private static bool Contains(string? value, string term) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
