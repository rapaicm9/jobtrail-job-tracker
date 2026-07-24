using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// A person a user deals with during a search - a recruiter, a hiring manager, a
/// referral. Linked to an application and/or a company (at least one), so it can
/// be the recruiter for a specific application, a general contact at a company, or
/// both. The application link is cascade-deleted with its application; the company
/// link nulls out if the company is removed, leaving the contact. <see cref="OwnerId"/>
/// is a non-FK reference to an Identity account - no cross-schema foreign key, ever.
/// <para>
/// <see cref="Name"/>, <see cref="Email"/>, <see cref="Phone"/> and <see cref="Notes"/>
/// are <b>personal data about a third party</b>. They are only ever read back to the
/// owner, must never be logged, put in an error message, or carried on an event, and
/// are erased with the account. No cross-user exposure, ever.
/// </para>
/// </summary>
internal sealed class Contact
{
    public Guid Id { get; set; }

    public UserId OwnerId { get; set; }

    /// <summary>The application this contact is attached to, if any.</summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>The company this contact belongs to, if any.</summary>
    public Guid? CompanyId { get; set; }

    public required string Name { get; set; }

    /// <summary>The part they play, if recorded.</summary>
    public ContactRole? Role { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the contact is next modified; null until then.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
