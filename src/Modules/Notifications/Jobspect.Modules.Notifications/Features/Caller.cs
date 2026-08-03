using Jobspect.SharedKernel;

namespace Jobspect.Modules.Notifications.Features;

/// <summary>Failures about the authenticated caller, shared across the module's slices.</summary>
internal static class Caller
{
    /// <summary>
    /// The request authenticated but its token carries no usable subject id, so there
    /// is no owner to scope the feed to - a 401, not an empty feed. The two are worth
    /// keeping apart here: an empty feed is the ordinary state of a new account, and
    /// silently returning one would hide a broken token behind a believable answer.
    /// </summary>
    public static readonly Error MissingSubject =
        Error.Unauthorized("auth.invalid_token", "The access token carries no usable subject.");
}
