using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobTrail.Modules.Applications.Features.CreateCustomField;

/// <summary>
/// Defines a custom field for the caller. The validator has already settled the
/// shape and that the options suit the type; what is left needs the database - the
/// account's field budget, and whether the name is already in use.
/// </summary>
internal sealed class CreateCustomFieldHandler(ApplicationsDbContext dbContext)
{
    public async Task<Result<CustomFieldResponse>> HandleAsync(
        UserId ownerId, CreateCustomFieldRequest request, CancellationToken cancellationToken)
    {
        // Archived fields count. Their recorded values are kept, so each one still
        // occupies a key in every application's bag - which is the thing the cap
        // is really bounding.
        var held = await dbContext.CustomFields.CountAsync(d => d.OwnerId == ownerId, cancellationToken);
        if (held >= FieldRules.CustomFieldsPerOwner)
        {
            return CustomFieldErrors.LimitReached(FieldRules.CustomFieldsPerOwner);
        }

        // The type parsed cleanly in the validator; re-reading it here is the same
        // answer, and it keeps the handler from depending on a validator's leftovers.
        var type = CustomFieldValidation.ParseType(request.Type)!.Value;
        var label = request.Label!.Trim();

        var definition = new CustomFieldDefinition
        {
            OwnerId = ownerId,
            Label = label,
            Type = type,
            Options = CustomFieldValidation.CleanOptions(type, request.Options),
        };

        dbContext.CustomFields.Add(definition);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The partial unique index is the truth, not a read taken beforehand:
            // two creates racing on the same name would both pass a pre-check and
            // only the constraint can decide between them.
            return CustomFieldErrors.LabelTaken(label);
        }

        return definition.ToResponse();
    }
}
