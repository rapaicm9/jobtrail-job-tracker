using Jobspect.SharedKernel;

namespace Jobspect.Modules.Analytics.Features;

/// <summary>Failures about the authenticated caller, shared across the module's slices.</summary>
internal static class Caller
{
    /// <summary>
    /// The request authenticated but its token carries no usable subject id, so
    /// there is no owner to scope the figures to - a 401, not an empty dashboard.
    /// </summary>
    public static readonly Error MissingSubject =
        Error.Unauthorized("auth.invalid_token", "The access token carries no usable subject.");
}
