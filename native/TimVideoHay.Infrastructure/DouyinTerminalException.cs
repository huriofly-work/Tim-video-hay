using TimVideoHay.Core;
namespace TimVideoHay.Infrastructure;

// A removed source or explicit anti-abuse block must never trigger account fallback.
public sealed class DouyinTerminalException(string message, bool blocked = false, DateTimeOffset? retryAt = null) : NeedsActionException(message)
{
    public bool Blocked { get; } = blocked;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}
