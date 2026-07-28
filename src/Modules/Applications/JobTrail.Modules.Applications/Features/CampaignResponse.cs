using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// A campaign as its owner sees it.
/// <para>
/// <see cref="ApplicationCount"/> is what makes a delete legible before it happens:
/// removing a campaign moves everything in it to the default, and a client cannot
/// say how much that is without counting pages of applications. It is always
/// populated rather than optional, so a client never has to tell "none" from "we
/// didn't look".
/// </para>
/// </summary>
internal sealed record CampaignResponse(
    Guid Id,
    string Name,
    bool IsDefault,
    int ApplicationCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class CampaignResponseMapping
{
    public static CampaignResponse ToResponse(this Campaign campaign, int applicationCount) => new(
        campaign.Id,
        campaign.Name,
        campaign.IsDefault,
        applicationCount,
        campaign.CreatedAt,
        campaign.UpdatedAt);
}
