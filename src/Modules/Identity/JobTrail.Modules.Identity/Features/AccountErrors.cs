using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Features;

/// <summary>Failures shared by the account slices.</summary>
internal static class AccountErrors
{
    /// <summary>
    /// The token authenticated, but its subject has no account row - the
    /// account was erased while an unexpired access token lived on. 404, not
    /// 403: the ownership check lives inside the lookup and a missing owner is
    /// indistinguishable from a resource that isn't there.
    /// </summary>
    public static readonly Error NotFound =
        Error.NotFound("account.not_found", "The account no longer exists.");
}
