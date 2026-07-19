namespace JobTrail.Api;

/// <summary>
/// Response headers for a JSON-only API (§5.4). No HSTS here: TLS terminates
/// at Caddy, which owns that header. The CSP is the API variant - this host
/// serves no HTML, so everything is denied and framing is refused outright.
/// </summary>
internal static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(static (context, next) =>
        {
            var headers = context.Response.Headers;

            // Browsers must not second-guess application/json or problem+json.
            headers.XContentTypeOptions = "nosniff";

            // API URLs can carry opaque ids; no referrer ever leaves the site.
            headers["Referrer-Policy"] = "no-referrer";

            // Nothing renders, nothing embeds. The legacy header covers old
            // browsers that predate frame-ancestors.
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers.XFrameOptions = "DENY";

            return next(context);
        });
}
