namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// The built-in fields a create and an update request share, so the one set of
/// shape rules (<see cref="ApplicationFieldValidation"/>) and value mapping
/// (<see cref="ApplicationFieldMapping"/>) applies to both. The dates and the
/// slice-specific fields (a create has no offer-decision deadline; an update
/// requires the applied date) stay on the concrete requests.
/// </summary>
internal interface IApplicationFields
{
    string? Role { get; }

    Guid? CompanyId { get; }

    string? CompanyName { get; }

    MoneyRequest? Compensation { get; }

    string? Location { get; }

    string? WorkMode { get; }

    string? PostingUrl { get; }

    string? Source { get; }

    string? CvLabel { get; }

    string? CoverLetterLabel { get; }
}
