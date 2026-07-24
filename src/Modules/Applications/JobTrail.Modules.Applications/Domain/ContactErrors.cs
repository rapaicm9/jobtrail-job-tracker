using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>Failures raised by the contact slices.</summary>
internal static class ContactErrors
{
    /// <summary>
    /// No contact with this id is owned by the caller. A contact owned by another
    /// user is reported the same way - a 404, never a 403 - so ownership stays
    /// unobservable. The id is safe to name; the contact's personal data is not.
    /// </summary>
    public static Error NotFound(Guid id) =>
        Error.NotFound("contact.not_found", $"No contact with id {id} exists.");

    /// <summary>
    /// The contact references an application the caller does not own (or that does
    /// not exist). A bad reference in the request is a validation failure - a 422.
    /// </summary>
    public static Error UnknownApplication(Guid applicationId) =>
        Error.Validation("contact.unknown_application", $"No application with id {applicationId} exists.");

    /// <summary>
    /// The contact references a company the caller does not own (or that does not
    /// exist). A bad reference in the request is a validation failure - a 422.
    /// </summary>
    public static Error UnknownCompany(Guid companyId) =>
        Error.Validation("contact.unknown_company", $"No company with id {companyId} exists.");
}
