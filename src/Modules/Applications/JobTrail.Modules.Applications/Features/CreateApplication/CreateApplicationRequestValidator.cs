using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features.CreateApplication;

/// <summary>
/// Shape-level checks on a create request, keyed by field so the client gets a
/// 422 pointing at what it sent. Ownership of a referenced company and resolving
/// the default campaign are the handler's job - they need the database - so this
/// only vets what can be judged from the request alone.
/// </summary>
internal static class CreateApplicationRequestValidator
{
    public static Dictionary<string, string[]>? Validate(CreateApplicationRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            errors.Add("role", "A role is required.");
        }
        else if (request.Role.Length > FieldRules.RoleMaxLength)
        {
            errors.Add("role", $"The role must be {FieldRules.RoleMaxLength} characters or fewer.");
        }

        ValidateCompany(request, errors);
        ValidateCompensation(request.Compensation, errors);

        if (request.WorkMode is not null
            && !(Enum.TryParse<WorkMode>(request.WorkMode, ignoreCase: true, out var workMode) && Enum.IsDefined(workMode)))
        {
            errors.Add("workMode", "The work mode must be one of Onsite, Hybrid or Remote.");
        }

        if (!string.IsNullOrWhiteSpace(request.PostingUrl))
        {
            if (request.PostingUrl.Length > FieldRules.PostingUrlMaxLength)
            {
                errors.Add("postingUrl", $"The posting URL must be {FieldRules.PostingUrlMaxLength} characters or fewer.");
            }
            else if (!FieldRules.IsAbsoluteHttpUrl(request.PostingUrl))
            {
                errors.Add("postingUrl", "The posting URL must be an absolute http or https URL.");
            }
        }

        if (request.Location is { Length: > FieldRules.LocationMaxLength })
        {
            errors.Add("location", $"The location must be {FieldRules.LocationMaxLength} characters or fewer.");
        }

        if (request.Source is { Length: > FieldRules.SourceMaxLength })
        {
            errors.Add("source", $"The source must be {FieldRules.SourceMaxLength} characters or fewer.");
        }

        if (request.CvLabel is { Length: > FieldRules.LabelMaxLength })
        {
            errors.Add("cvLabel", $"The CV label must be {FieldRules.LabelMaxLength} characters or fewer.");
        }

        if (request.CoverLetterLabel is { Length: > FieldRules.LabelMaxLength })
        {
            errors.Add("coverLetterLabel", $"The cover-letter label must be {FieldRules.LabelMaxLength} characters or fewer.");
        }

        return errors.ToResultOrNull();
    }

    private static void ValidateCompany(CreateApplicationRequest request, ValidationErrors errors)
    {
        var hasName = !string.IsNullOrWhiteSpace(request.CompanyName);
        if (request.CompanyId is not null && hasName)
        {
            errors.Add("companyName", "Provide either a company id or a company name, not both.");
        }
        else if (hasName && request.CompanyName!.Length > FieldRules.CompanyNameMaxLength)
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
}
