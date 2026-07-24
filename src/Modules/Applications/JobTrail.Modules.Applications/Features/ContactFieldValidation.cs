using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// The shape rules a contact's fields share between create and update, keyed to
/// the field a client sent. A contact must be linked to an application, a company,
/// or both - a person floating free of either isn't a contact in a job search - so
/// that, the name, and the shape of role/email/phone are checked here. Whether the
/// linked application or company is actually the caller's own needs the database,
/// so it stays in the handler.
/// </summary>
internal static class ContactFieldValidation
{
    public static void Validate(IContactFields fields, ValidationErrors errors)
    {
        if (fields.ApplicationId is null && fields.CompanyId is null)
        {
            errors.Add("applicationId", "A contact must be linked to an application, a company, or both.");
        }

        if (string.IsNullOrWhiteSpace(fields.Name))
        {
            errors.Add("name", "A name is required.");
        }
        else if (fields.Name.Length > FieldRules.ContactNameMaxLength)
        {
            errors.Add("name", $"The name must be {FieldRules.ContactNameMaxLength} characters or fewer.");
        }

        if (fields.Role is not null
            && !(Enum.TryParse<ContactRole>(fields.Role, ignoreCase: true, out var role) && Enum.IsDefined(role)))
        {
            errors.Add("role", "The role must be one of Recruiter, HiringManager, Interviewer, Referral or Other.");
        }

        ValidateEmail(fields.Email, errors);
        ValidatePhone(fields.Phone, errors);

        if (fields.Notes is { Length: > FieldRules.NotesMaxLength })
        {
            errors.Add("notes", $"The notes must be {FieldRules.NotesMaxLength} characters or fewer.");
        }
    }

    private static void ValidateEmail(string? email, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        if (email.Length > FieldRules.EmailMaxLength)
        {
            errors.Add("email", $"The email must be {FieldRules.EmailMaxLength} characters or fewer.");
        }
        else if (!FieldRules.IsPlausibleEmail(email))
        {
            errors.Add("email", "The email address is not valid.");
        }
    }

    private static void ValidatePhone(string? phone, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        if (phone.Length > FieldRules.PhoneMaxLength)
        {
            errors.Add("phone", $"The phone number must be {FieldRules.PhoneMaxLength} characters or fewer.");
        }
        else if (!FieldRules.IsPlausiblePhone(phone))
        {
            errors.Add("phone", "The phone number is not valid.");
        }
    }
}
