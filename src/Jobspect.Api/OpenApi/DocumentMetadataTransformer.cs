using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Jobspect.Api.OpenApi;

/// <summary>
/// The document's own header: what this API is, and where it lives.
/// </summary>
/// <remarks>
/// Clearing <see cref="OpenApiDocument.Servers"/> is the load-bearing part. The
/// generator populates it from the request that fetched the document, so the
/// same contract describes a different server on every host and - because the
/// dev port is assigned per run - on every run. An absent list means "the origin
/// this document came from", which is both true everywhere and stable enough to
/// commit and diff.
/// </remarks>
internal sealed class DocumentMetadataTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = "Jobspect API";
        document.Info.Description =
            "Job-application tracking: applications and their pipeline, the companies, contacts and interviews "
            + "around them, reminders, and the analytics over the lot. Every route is scoped to the calling "
            + "account - a resource owned by another user is reported as absent, not as forbidden.";

        // The version segment is part of every path, so a server URL carrying one
        // would double it. Left empty on purpose; see the remarks above.
        document.Servers?.Clear();

        return Task.CompletedTask;
    }
}
