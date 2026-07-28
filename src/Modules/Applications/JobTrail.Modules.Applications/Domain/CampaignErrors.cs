using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>Failures raised by the campaign slices.</summary>
internal static class CampaignErrors
{
    /// <summary>
    /// No campaign with this id is owned by the caller. A campaign owned by another
    /// user is reported the same way - a 404, never a 403 - so ownership stays
    /// unobservable.
    /// </summary>
    public static Error NotFound(Guid id) =>
        Error.NotFound("campaign.not_found", $"No campaign with id {id} exists.");

    /// <summary>
    /// The caller already has a campaign by this name. A conflict rather than a
    /// validation failure: the request was well formed, the name is taken.
    /// </summary>
    public static Error NameTaken(string name) =>
        Error.Conflict("campaign.name_taken", $"A campaign named '{name}' already exists.");

    /// <summary>The account is at its campaign limit. The default counts - it is one of them.</summary>
    public static Error LimitReached(int limit) =>
        Error.Conflict("campaign.limit_reached", $"An account may hold {limit} campaigns.");

    /// <summary>
    /// The default campaign cannot be deleted. Deleting a campaign moves its
    /// applications to the default, so the default is the one campaign there is
    /// nowhere to move them to - and every application must belong to one.
    /// </summary>
    public static readonly Error DefaultNotDeletable = Error.Conflict(
        "campaign.default_not_deletable",
        "The default campaign cannot be deleted. Rename it, or delete the others.");
}
