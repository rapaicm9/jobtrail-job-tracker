using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using StackExchange.Redis;

namespace JobTrail.Api;

/// <summary>
/// Persists the Data Protection key ring to Redis so antiforgery and any
/// future cookie payloads survive redeploys and are shared across instances -
/// forgetting this invalidates them all on every container restart (§5.4).
/// Redis is the stack's home for externalized state (§10.2); the JWTs
/// themselves never touch Data Protection (ES256, own keys).
/// </summary>
internal static class DataProtectionConfiguration
{
    private const string KeyRingRedisKey = "jobtrail:dataprotection:keys";

    public static IHostApplicationBuilder AddApiDataProtection(this IHostApplicationBuilder builder)
    {
        // Aspire-wired multiplexer for the AppHost's "cache" resource: health
        // check, telemetry and the injected connection string come with it.
        builder.AddRedisClient(connectionName: "cache");

        builder.Services
            .AddDataProtection()
            // A stable name, not the assembly name: keys must keep decrypting
            // after a rename or a second host joins the ring.
            .SetApplicationName("jobtrail");

        // The repository is attached via options so the multiplexer comes from
        // DI - the PersistKeysToStackExchangeRedis overloads want an instance
        // that does not exist yet at configuration time.
        builder.Services
            .AddOptions<KeyManagementOptions>()
            .Configure<IConnectionMultiplexer>((options, redis) =>
                options.XmlRepository = new RedisXmlRepository(() => redis.GetDatabase(), KeyRingRedisKey));

        return builder;
    }
}
