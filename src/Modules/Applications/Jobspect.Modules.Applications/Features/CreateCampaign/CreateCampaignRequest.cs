namespace Jobspect.Modules.Applications.Features.CreateCampaign;

/// <summary>
/// The fields to open a campaign. There is no <c>isDefault</c> here: the default is
/// the campaign created with the account, and it stays the default for the account's
/// life. Every campaign created through this endpoint is an addition to it.
/// </summary>
internal sealed record CreateCampaignRequest(string? Name);
