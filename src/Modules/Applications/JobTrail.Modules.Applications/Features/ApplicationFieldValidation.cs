using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// The shape rules the built-in fields share between create and update, keyed to
/// the field a client sent. Slice-specific rules (an update's required applied
/// date, its offer-decision-deadline guard) live with their own slice; this is
/// only what both agree on.
/// </summary>
internal static class ApplicationFieldValidation
{
    public static void Validate(IApplicationFields fields, ValidationErrors errors)
    {
        ValidateRole(fields.Role, errors);
        ValidateCompany(fields.CompanyId, fields.CompanyName, errors);
        ValidateCompensation(fields.Compensation, errors);
        ValidateWorkMode(fields.WorkMode, errors);
        ValidatePostingUrl(fields.PostingUrl, errors);

        ValidateLength(fields.Location, FieldRules.LocationMaxLength, "location", "location", errors);
        ValidateLength(fields.Source, FieldRules.SourceMaxLength, "source", "source", errors);
        ValidateLength(fields.CvLabel, FieldRules.LabelMaxLength, "cvLabel", "CV label", errors);
        ValidateLength(fields.CoverLetterLabel, FieldRules.LabelMaxLength, "coverLetterLabel", "cover-letter label", errors);
    }

    private static void ValidateRole(string? role, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            errors.Add("role", "A role is required.");
        }
        else if (role.Length > FieldRules.RoleMaxLength)
        {
            errors.Add("role", $"The role must be {FieldRules.RoleMaxLength} characters or fewer.");
        }
    }

    private static void ValidateCompany(Guid? companyId, string? companyName, ValidationErrors errors)
    {
        var hasName = !string.IsNullOrWhiteSpace(companyName);
        if (companyId is not null && hasName)
        {
            errors.Add("companyName", "Provide either a company id or a company name, not both.");
        }
        else if (hasName && companyName!.Length > FieldRules.CompanyNameMaxLength)
        {
            errors.Add("companyName", $"The company name must be {FieldRules.CompanyNameMaxLength} characters or fewer.");
        }
    }

    private static void ValidateCompensation(MoneyRequest? compensation, ValidationErrors errors)
    {
        if (compensation is null)
        {
            return;
        }

        if (compensation.Amount < 0)
        {
            errors.Add("compensation", "The compensation amount cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(compensation.Currency) || !FieldRules.IsCurrencyCode(compensation.Currency))
        {
            errors.Add("compensation", "The compensation currency must be a three-letter code, e.g. \"EUR\".");
        }
    }

    private static void ValidateWorkMode(string? workMode, ValidationErrors errors)
    {
        if (workMode is not null
            && !(Enum.TryParse<WorkMode>(workMode, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)))
        {
            errors.Add("workMode", "The work mode must be one of Onsite, Hybrid or Remote.");
        }
    }

    private static void ValidatePostingUrl(string? postingUrl, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(postingUrl))
        {
            return;
        }

        if (postingUrl.Length > FieldRules.PostingUrlMaxLength)
        {
            errors.Add("postingUrl", $"The posting URL must be {FieldRules.PostingUrlMaxLength} characters or fewer.");
        }
        else if (!FieldRules.IsAbsoluteHttpUrl(postingUrl))
        {
            errors.Add("postingUrl", "The posting URL must be an absolute http or https URL.");
        }
    }

    private static void ValidateLength(string? value, int max, string field, string label, ValidationErrors errors)
    {
        if (value is not null && value.Length > max)
        {
            errors.Add(field, $"The {label} must be {max} characters or fewer.");
        }
    }
}
