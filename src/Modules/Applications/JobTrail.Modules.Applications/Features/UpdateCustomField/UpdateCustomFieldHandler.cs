using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobTrail.Modules.Applications.Features.UpdateCustomField;

/// <summary>
/// Replaces the editable parts of one of the caller's custom fields - its label,
/// its options, and whether it is retired. Ownership is the query, so another
/// user's field is a 404.
/// <para>
/// The type is not among them, and this is where that costs something: the options
/// have to be judged against a type the request never carried, so the definition is
/// loaded first and the rule applied to what was stored.
/// </para>
/// </summary>
internal sealed class UpdateCustomFieldHandler(ApplicationsDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<CustomFieldResponse>> HandleAsync(
        UserId ownerId, Guid id, UpdateCustomFieldRequest request, CancellationToken cancellationToken)
    {
        var definition = await dbContext.CustomFields
            .FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == ownerId, cancellationToken);
        if (definition is null)
        {
            return CustomFieldErrors.NotFound(id);
        }

        if (CustomFieldValidation.OptionsProblemFor(definition.Type, request.Options) is { } problem)
        {
            return CustomFieldErrors.OptionsNotAllowed(problem);
        }

        var label = request.Label!.Trim();

        definition.Label = label;
        definition.Options = CustomFieldValidation.CleanOptions(definition.Type, request.Options);
        definition.IsArchived = request.IsArchived;
        definition.UpdatedAt = timeProvider.GetUtcNow();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Renamed onto a name another live field already holds. Unarchiving a
            // field whose name was reused while it was away lands here too, which
            // is the right answer: both cannot be offered under one name.
            return CustomFieldErrors.LabelTaken(label);
        }

        return definition.ToResponse();
    }
}
