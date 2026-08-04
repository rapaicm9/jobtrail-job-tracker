using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Jobspect.Api.OpenApi;

/// <summary>
/// Groups operations by the resource they act on, taken from the path.
/// </summary>
/// <remarks>
/// Left alone, the generator tags each operation with the name of the class that
/// declares it - which, in a codebase of one endpoint per class, is one tag per
/// operation. Tags are what client generators name their classes after, so that
/// default produces a client of forty-odd single-method services rather than one
/// service per resource. The path already says which resource an operation
/// belongs to, so this reads it from there instead.
/// </remarks>
internal sealed class ResourceTagTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        var resources = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, item) in document.Paths)
        {
            if (ResourceOf(path) is not { } resource || item.Operations is null)
            {
                continue;
            }

            resources.Add(resource);

            foreach (var operation in item.Operations.Values)
            {
                operation.Tags?.Clear();
                operation.Tags ??= new HashSet<OpenApiTagReference>();
                operation.Tags.Add(new OpenApiTagReference(resource, document));
            }
        }

        // Rebuilt rather than appended to: the per-class tags the generator
        // declared are no longer referenced by anything.
        document.Tags = new HashSet<OpenApiTag>(resources.Select(resource => new OpenApiTag { Name = resource }));

        return Task.CompletedTask;
    }

    /// <summary>
    /// The segment after the version - <c>/api/v1/applications/{id}/interviews</c>
    /// is an operation on applications. Sub-resources deliberately fold into
    /// their parent: an interview is only ever reached through the application
    /// that holds it, so splitting them would name a client that cannot be used
    /// on its own.
    /// </summary>
    private static string? ResourceOf(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments is ["api", _, var resource, ..] ? resource : null;
    }
}
