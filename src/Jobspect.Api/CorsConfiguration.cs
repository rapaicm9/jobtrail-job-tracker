using Microsoft.Net.Http.Headers;

namespace Jobspect.Api;

/// <summary>
/// Exact-origin allowlist, bound from <c>Cors:AllowedOrigins</c>. With nothing
/// configured every cross-origin call is refused, which is exactly what production
/// does - it names no origin at all.
/// <para>
/// <b>No browser is a client of this API, and the policy says so rather than
/// half-supporting one.</b> The web client keeps its tokens in Next.js route
/// handlers and the browser never holds one (ADR-0003), so a page has nothing to
/// authenticate with. The calls that do arrive come from Node, which sends no
/// <c>Origin</c> and asks for no preflight, and from native mobile, which sends
/// none either. CORS therefore governs no traffic this system produces; it is here
/// so that a cross-origin call from anywhere else is refused outright.
/// </para>
/// <para>
/// Which is why <c>Authorization</c> is not in the allowlist. Admitting it
/// described a browser carrying a bearer token - the one client the token model
/// rules out - and described it incompletely at that: a keyed mutation's
/// <c>Idempotency-Key</c> was missing, and no response header was exposed, so
/// neither a <c>Location</c> nor a <c>Retry-After</c> could be read. Completing
/// that set would have made the forbidden client work. Naming only what an
/// anonymous JSON request needs leaves a policy that is true, and a browser client
/// - if there is ever one - arrives with its own headers and its own reasons.
/// </para>
/// </summary>
internal static class CorsConfiguration
{
    private const string ConfigurationKey = "Cors:AllowedOrigins";

    public static IHostApplicationBuilder AddApiCors(this IHostApplicationBuilder builder)
    {
        var origins = builder.Configuration.GetSection(ConfigurationKey).Get<string[]>() ?? [];

        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
            .WithOrigins(origins)
            // A JSON body, and nothing beyond it. No credentials either: this host
            // has no cookie authentication, and no token a browser could hold.
            .WithHeaders(HeaderNames.ContentType)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

        return builder;
    }
}
