using System.Security.Cryptography;
using JobTrail.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL 18 + one Redis container and one running host for the whole
/// collection. Tests isolate by data (unique email per test), not by respawn -
/// cheap and sufficient while Identity's flows only ever touch their own rows;
/// revisit with Respawn when the Applications module brings cross-test tables.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    // postgres:18 because the migrations lean on DB-side uuidv7().
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8.2").Build();

    private JobTrailApiFactory? _factory;

    public ApiFixture()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PrivateKeyPem = ecdsa.ExportPkcs8PrivateKeyPem();
        PublicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();
    }

    public string PrivateKeyPem { get; }

    public string PublicKeyPem { get; }

    public HttpClient CreateClient() => Factory.CreateClient();

    private JobTrailApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <summary>
    /// The baseline the shared host runs with; a test needing different knobs
    /// (the throttling test) copies and overrides against the same containers.
    /// </summary>
    public Dictionary<string, string?> BuildSettings() => new()
    {
        ["ConnectionStrings:jobtrail"] = _postgres.GetConnectionString(),
        ["ConnectionStrings:cache"] = _redis.GetConnectionString(),
        ["Identity:Jwt:PrivateKeyPem"] = PrivateKeyPem,
        ["Identity:Jwt:PublicKeyPem"] = PublicKeyPem,
        // Budgets high enough that the suite itself is never the client that
        // gets throttled; the throttling test dials its own down.
        ["RateLimiting:GlobalPermitLimit"] = "10000",
        ["RateLimiting:AuthPermitLimit"] = "10000",
    };

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _factory = new JobTrailApiFactory(BuildSettings());

        // Deploy-time migrations, test-time equivalent: apply the Identity
        // module's migrations before the first request needs the schema.
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>()
            .Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
