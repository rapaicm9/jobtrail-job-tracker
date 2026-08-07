using Asp.Versioning.OpenApi;

namespace Jobspect.Api.OpenApi;

/// <summary>
/// The described contract, one document per API version.
/// </summary>
/// <remarks>
/// <para>
/// The delegate runs once per version discovered by the API explorer, so a v2
/// added later is described without a line changing here.
/// </para>
/// <para>
/// The document is served in Development only. It is generated from the live
/// endpoint graph, so it reports every route the host has - including the
/// developer shortcuts - and there is no reader for it in production.
/// </para>
/// </remarks>
internal static class OpenApiConfiguration
{
    /// <summary>
    /// The document is served in both formats, which costs one extra route: the
    /// format is chosen from the registered pattern's suffix, not from the
    /// request, so a single route with an extension parameter would silently
    /// serve JSON under a <c>.yaml</c> URL.
    /// </summary>
    public const string JsonRoute = "/openapi/{documentName}.json";

    /// <inheritdoc cref="JsonRoute"/>
    public const string YamlRoute = "/openapi/{documentName}.yaml";

    public static void Describe(VersionedOpenApiOptions options)
    {
        var bearer = new BearerSecurityTransformer();

        options.Document.AddDocumentTransformer(new DocumentMetadataTransformer());
        options.Document.AddDocumentTransformer(new ResourceTagTransformer());
        options.Document.AddDocumentTransformer(bearer);
        options.Document.AddOperationTransformer(bearer);
        options.Document.AddSchemaTransformer(new EnumSchemaTransformer());
    }
}
