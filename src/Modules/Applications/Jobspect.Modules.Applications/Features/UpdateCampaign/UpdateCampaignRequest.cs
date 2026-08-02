namespace Jobspect.Modules.Applications.Features.UpdateCampaign;

/// <summary>
/// The editable fields of a campaign, which is its name and nothing else.
/// <para>
/// There is deliberately no <c>isDefault</c>: which campaign is the default is
/// fixed when the account is created. It is where a deleted campaign's applications
/// are sent and the one campaign that always exists, so moving the flag would be a
/// change to two campaigns at once rather than an edit to this one. Renaming covers
/// what a user actually wants from it - the default can be called whatever their
/// search is called.
/// </para>
/// </summary>
internal sealed record UpdateCampaignRequest(string? Name);
