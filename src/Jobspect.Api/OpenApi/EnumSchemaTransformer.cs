using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Jobspect.Api.OpenApi;

/// <summary>
/// Keeps a named enum schema to its members, so nothing but the members ends up
/// in the type a client generates from it.
/// </summary>
/// <remarks>
/// <para>
/// An enum this API only ever returns as nullable - a contact's role, a work
/// mode, the kind of a stage transition - is described from the nullable type,
/// so <c>null</c> lands in the member list. The document then says the same
/// thing twice, because the property referencing it is separately wrapped as
/// "null or this". The wrapper is the correct half: nullability is a fact about
/// one property, while the member set is a fact about the vocabulary, and a
/// generated <c>ContactRole</c> that admits null would disagree with the enum
/// declared in this codebase.
/// </para>
/// <para>
/// Every reference the generator emits for such a property carries that wrapper,
/// so dropping the member loses nothing: the field still accepts null and the
/// type no longer claims to.
/// </para>
/// </remarks>
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        // A null member is a JSON null, which arrives as a null element rather
        // than as a JsonNode holding one.
        if (schema.Enum is { } members && members.Any(member => member is null))
        {
            schema.Enum = [.. members.Where(member => member is not null)];
        }

        return Task.CompletedTask;
    }
}
