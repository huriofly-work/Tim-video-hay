using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

// An HTTP-only session cannot complete the site's browser verification.
// This requests the anonymous browser, never an automatic account switch.
public sealed class DouyinGuestVerificationException(string message, bool requiresLogin = false) : NeedsActionException(message)
{
    public bool RequiresLogin { get; } = requiresLogin;
}
