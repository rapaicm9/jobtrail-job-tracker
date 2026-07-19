using Microsoft.Net.Http.Headers;

namespace JobTrail.Api;

/// <summary>
/// Exact-origin allowlist (§5.4), bound from <c>Cors:AllowedOrigins</c>. Dev
/// settings list the Next.js origin; production sets it by env var. With
/// nothing configured every cross-origin call is refused, which is the right
/// failure mode for an API whose clients are a same-machine BFF and native
/// mobile apps (which send no Origin at all).
/// </summary>
internal static class CorsConfiguration
{
    private const string ConfigurationKey = "Cors:AllowedOrigins";

    public static IHostApplicationBuilder AddApiCors(this IHostApplicationBuilder builder)
    {
        var origins = builder.Configuration.GetSection(ConfigurationKey).Get<string[]>() ?? [];

        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
            .WithOrigins(origins)
            // The narrow set an SPA talking JSON actually needs - tokens travel
            // in the Authorization header, never in cookies, so no credentials.
            .WithHeaders(HeaderNames.ContentType, HeaderNames.Authorization)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

        return builder;
    }
}
