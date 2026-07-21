using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Features.GetAccount;
using JobTrail.Modules.Identity.Features.Login;
using JobTrail.Modules.Identity.Features.Logout;
using JobTrail.Modules.Identity.Features.LogoutAll;
using JobTrail.Modules.Identity.Features.Refresh;
using JobTrail.Modules.Identity.Features.Register;
using JobTrail.Modules.Identity.Features.UpdateAccount;
using JobTrail.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JobTrail.Modules.Identity;

/// <summary>
/// The Identity module's composition surface. A host calls
/// <see cref="AddIdentityModule"/> to register the account store,
/// <see cref="AddIdentityJwtAuthentication"/> to validate the module's access
/// tokens, and <see cref="MapIdentityEndpoints"/> to expose the auth slices;
/// everything the module owns stays internal behind them.
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

                // The password policy of record. RegisterRequestValidator
                // mirrors it for field-keyed 422s; keep the two in step.
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddEntityFrameworkStores<IdentityModuleDbContext>();

        AddTokenModel(builder);

        // The slice handlers - plain classes, invoked directly by the endpoints.
        builder.Services.AddScoped<RegisterHandler>();
        builder.Services.AddScoped<LoginHandler>();
        builder.Services.AddScoped<LogoutAllHandler>();
        builder.Services.AddScoped<GetAccountHandler>();
        builder.Services.AddScoped<UpdateAccountHandler>();

        return builder;
    }

    /// <summary>
    /// Registers the JwtBearer scheme that validates this module's access
    /// tokens (ADR-0003: public key only, full rigor, per-request token-version
    /// check). Lives here rather than in the host so the signing-key provider
    /// and claim names never leak across the module boundary; the Api host just
    /// calls this and adds authorization.
    /// </summary>
    public static IHostApplicationBuilder AddIdentityJwtAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<ISigningKeyProvider, IOptions<JwtOptions>>(JwtBearerConfiguration.Configure);

        return builder;
    }

    /// <summary>
    /// Maps the auth slices onto the host's versioned API group, under
    /// <c>/identity</c>. Returns the group itself so the host can layer
    /// cross-cutting policy over it (rate limiting, output caching) without the
    /// module naming host concerns.
    /// </summary>
    public static RouteGroupBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        var identity = api.MapGroup("/identity");

        RegisterEndpoint.Map(identity);
        LoginEndpoint.Map(identity);
        RefreshEndpoint.Map(identity);
        LogoutEndpoint.Map(identity);
        LogoutAllEndpoint.Map(identity);

        return identity;
    }

    /// <summary>
    /// Maps the account self-service slices onto the host's versioned API group,
    /// under <c>/account</c>. A sibling of the auth group, not a child: these are
    /// authenticated profile operations, so they take the host's general per-IP
    /// budget rather than the auth surface's stricter window. Returns the group
    /// so the host can layer its own policy.
    /// </summary>
    public static RouteGroupBuilder MapAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var account = api.MapGroup("/account");

        GetAccountEndpoint.Map(account);
        UpdateAccountEndpoint.Map(account);

        return account;
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
