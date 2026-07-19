using Microsoft.AspNetCore.HttpOverrides;

// The obsoleted Microsoft.AspNetCore.HttpOverrides.IPNetwork still exists and
// collides with the System.Net replacement, so the alias picks the new one.
using IPNetwork = System.Net.IPNetwork;

namespace JobTrail.Api;

/// <summary>
/// Restores the real client address and scheme behind the reverse proxy, so
/// per-IP rate limiting buckets the caller rather than Caddy. Trust is opt-in:
/// only proxies inside the CIDRs from <c>ReverseProxy:KnownNetworks</c> may
/// rewrite the connection (plus loopback, which ASP.NET trusts by default) -
/// with nothing configured, forwarded headers from strangers are ignored,
/// which is exactly right for local runs without a proxy.
/// </summary>
internal static class ForwardedHeadersConfiguration
{
    private const string ConfigurationKey = "ReverseProxy:KnownNetworks";

    public static IHostApplicationBuilder AddApiForwardedHeaders(this IHostApplicationBuilder builder)
    {
        var networks = builder.Configuration.GetSection(ConfigurationKey).Get<string[]>() ?? [];

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // KnownIPNetworks is the .NET 10 replacement for the obsoleted
            // KnownNetworks/HttpOverrides.IPNetwork pair; System.Net.IPNetwork
            // parses standard CIDR notation.
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(IPNetwork.Parse(network));
            }
        });

        return builder;
    }
}
