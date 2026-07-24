namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// The fields a create and an update contact request share - which is all of them,
/// so the one set of shape rules (<see cref="ContactFieldValidation"/>) applies to
/// both. Ownership of the referenced application and company is the handler's job;
/// it needs the database.
/// </summary>
internal interface IContactFields
{
    Guid? ApplicationId { get; }

    Guid? CompanyId { get; }

    string? Name { get; }

    string? Role { get; }

    string? Email { get; }

    string? Phone { get; }

    string? Notes { get; }
}
