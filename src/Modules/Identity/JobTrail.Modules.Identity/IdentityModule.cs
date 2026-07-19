using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace JobTrail.Modules.Identity;

/// <summary>
/// The Identity module's composition surface. A host calls
/// <see cref="AddIdentityModule"/> to register the account store; everything the
/// module owns stays internal behind it.
/// </summary>
public static class IdentityModule
{
    public static IHostApplicationBuilder AddIdentityModule(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("jobtrail")
            ?? throw new InvalidOperationException(
                "Connection string 'jobtrail' is not configured. It is injected by the AppHost.");

        builder.Services.AddDbContext<IdentityModuleDbContext>(options =>
            NpgsqlContextConfiguration.Configure(options, connectionString, IdentityModuleDbContext.Schema));

        // Aspire adds health checks, a retrying execution strategy and telemetry
        // to the context registered above, without owning its configuration.
        builder.EnrichNpgsqlDbContext<IdentityModuleDbContext>();

        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddEntityFrameworkStores<IdentityModuleDbContext>();

        AddTokenModel(builder);

        return builder;
    }

    /// <summary>
    /// Registers the token model (ADR-0003). The endpoints that call it arrive in
    /// the auth-endpoints slice; wiring it here keeps that slice to routing and
    /// host configuration. Key material is read lazily, so a host without keys
    /// configured still starts - nothing signs a token until an endpoint runs.
    /// </summary>
    private static void AddTokenModel(IHostApplicationBuilder builder)
    {
        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

        // The clock abstraction. TryAdd so a host is free to register a fake first.
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Stateless and key-holding: one instance for the app.
        builder.Services.AddSingleton<ISigningKeyProvider, EcdsaSigningKeyProvider>();
        builder.Services.AddSingleton<AccessTokenIssuer>();

        // Persistence-touching: scoped to the request's DbContext.
        builder.Services.AddScoped<IRefreshTokenStore, EfRefreshTokenStore>();
        builder.Services.AddScoped<IUserTokenVersionReader, EfUserTokenVersionReader>();
        builder.Services.AddScoped<RefreshTokenService>();
        builder.Services.AddScoped<TokenService>();
    }
}
