namespace TimVideoHay.Core;

public enum JobIssueKind { General, Authentication, UnknownResult, MissingFolder, Duplicate, InvalidData, Configuration, SourceUnavailable, RateLimited, Network, FileAccess }
public sealed record JobIssue(string Id, JobIssueKind Kind, JobActions AffectedActions, string Message, DateTimeOffset Created,
    string? DependencyKey = null, DateTimeOffset? RetryAt = null)
{
    public static JobIssue Create(VideoJob job, Exception error, string message)
    {
        var kind = error is NeedsAuthException ? JobIssueKind.Authentication
            : error is NeedsActionException action ? action.Kind
            : error is HttpRequestException or OperationCanceledException ? JobIssueKind.Network
            : error is ArgumentException or InvalidDataException ? JobIssueKind.InvalidData
            : error is IOException or UnauthorizedAccessException ? JobIssueKind.FileAccess : JobIssueKind.General;
        // Unknown remote writes must always be checked, regardless of the original exception type.
        if (job.UnknownResult && kind != JobIssueKind.Authentication) kind = JobIssueKind.UnknownResult;
        var affected = job.StageAction == JobActions.None ? job.Work : job.StageAction;
        string? dependency = kind == JobIssueKind.Authentication ? CredentialKey(job, affected)
            : kind == JobIssueKind.RateLimited && error is RetryLaterException retry ? retry.DependencyKey : null;
        return new(Guid.NewGuid().ToString("N"), kind, affected, message, DateTimeOffset.UtcNow, dependency, (error as RetryLaterException)?.RetryAt);
    }
    public static string? CredentialKey(VideoJob job, JobActions action)
    {
        if (action.HasFlag(JobActions.WriteSheet) && job.Sheet is not null) return "sheets:" + job.Sheet.CredentialId;
        if (action.HasFlag(JobActions.UploadYouTube) && job.Channel is not null) return "youtube:" + job.Channel.CredentialId;
        return null;
    }
}
public sealed record JobDecisionReceipt(string IssueId, AttentionAction Action, DateTimeOffset At);
public sealed class RetryLaterException(string message, DateTimeOffset retryAt, string dependencyKey)
    : NeedsActionException(message, JobIssueKind.RateLimited)
{
    public DateTimeOffset RetryAt { get; } = retryAt;
    public string DependencyKey { get; } = dependencyKey;
}
