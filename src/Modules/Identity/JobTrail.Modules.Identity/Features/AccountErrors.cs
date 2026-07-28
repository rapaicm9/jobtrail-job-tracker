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

    /// <summary>
    /// The caller asked for an export without the entitlement to one. The route
    /// policy answers this first and a caller will never see it; it exists so the
    /// handler refuses on its own terms rather than trusting that it was only ever
    /// reached through the endpoint that guards it.
    /// </summary>
    public static readonly Error ExportNotEntitled =
        Error.Forbidden("account.export_not_entitled", "Exporting your data requires Pro.");
}
