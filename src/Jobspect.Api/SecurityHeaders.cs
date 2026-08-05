namespace Jobspect.Api;

/// <summary>
/// Marks the one endpoint that answers with a page rather than JSON, so the
/// policy below can widen for it and only it. Applied by the API reference's
/// mapping, which exists in Development alone.
/// </summary>
internal sealed class RendersApiReference;

/// <summary>
/// Response headers for a JSON-only API (§5.4). No HSTS here: TLS terminates
/// at Caddy, which owns that header. The CSP is the API variant - this host
/// serves no HTML, so everything is denied and framing is refused outright.
/// </summary>
internal static class SecurityHeaders
{
    /// <summary>Nothing renders, nothing embeds.</summary>
    private const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// What the API reference needs to draw itself, and nothing beyond it. No
    /// host is named: the package serves its own bundle, and the reference is
    /// configured to skip the webfonts it would otherwise pull from a CDN, so
    /// every source is this origin. <c>'unsafe-inline'</c> covers the page's
    /// bootstrap script and the styles the UI injects at runtime;
    /// <c>connect-src 'self'</c> is what lets the page read the document and
    /// call this API while refusing it any other destination.
    /// </summary>
    private const string ApiReferencePolicy =
        "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; "
        + "script-src 'self' 'unsafe-inline'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "font-src 'self' data:; "
        + "img-src 'self' data:; "
        + "connect-src 'self'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(static (context, next) =>
        {
            var headers = context.Response.Headers;

            // Browsers must not second-guess application/json or problem+json.
            headers.XContentTypeOptions = "nosniff";

            // API URLs can carry opaque ids; no referrer ever leaves the site.
            headers["Referrer-Policy"] = "no-referrer";

            // Both policies live here rather than at the endpoint that needs the
            // wider one: a reader auditing what this host allows should find the
            // whole answer in one file, including the exception.
            headers.ContentSecurityPolicy =
                context.GetEndpoint()?.Metadata.GetMetadata<RendersApiReference>() is null
                    ? ApiPolicy
                    : ApiReferencePolicy;

            // The legacy header covers old browsers that predate frame-ancestors.
            // Unconditional: the reference has no more business being framed.
            headers.XFrameOptions = "DENY";

            return next(context);
        });
}
