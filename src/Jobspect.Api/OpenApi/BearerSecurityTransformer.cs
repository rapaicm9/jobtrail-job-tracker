using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Jobspect.Api.OpenApi;

/// <summary>
/// Describes the bearer scheme once on the document, then marks the operations
/// that actually need it. Both halves live in one type because they share the
/// scheme's name: declaring it and referencing it from two places is how a
/// document ends up with a requirement pointing at a scheme that isn't there.
/// </summary>
/// <remarks>
/// The generator infers nothing about authorization, so without this a client
/// generated from the document would send no <c>Authorization</c> header and
/// every authenticated route would answer 401.
/// </remarks>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string SchemeName = "bearer";

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "The access token returned by /identity/register, /identity/login or /identity/refresh.",
        };

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // Per operation rather than document-wide: a blanket requirement would
        // describe register, login and refresh as needing the very token they
        // exist to hand out.
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
        });

        return Task.CompletedTask;
    }
}
