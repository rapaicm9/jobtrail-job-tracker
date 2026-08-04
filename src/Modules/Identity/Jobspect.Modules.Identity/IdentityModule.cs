using Jobspect.Infrastructure.Events;
using Jobspect.Infrastructure.Outbox;
using Jobspect.Infrastructure.Persistence;
using Jobspect.Modules.Identity.Authentication;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Identity.Domain;
using Jobspect.Modules.Identity.Features.DeleteAccount;
using Jobspect.Modules.Identity.Features.ExportAccount;
using Jobspect.Modules.Identity.Features.GetAccount;
using Jobspect.Modules.Identity.Features.Login;
using Jobspect.Modules.Identity.Features.Logout;
using Jobspect.Modules.Identity.Features.LogoutAll;
using Jobspect.Modules.Identity.Features.Refresh;
using Jobspect.Modules.Identity.Features.Register;
using Jobspect.Modules.Identity.Features.UpdateAccount;
using Jobspect.Modules.Identity.Persistence;
using Jobspect.SharedKernel;
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

namespace Jobspect.Modules.Identity;

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
        // The store and the profile read, composed through the narrow method rather
        // than repeated here, so the two ways in cannot register the context
        // differently - or twice.
        builder.AddIdentityProfileQuery();

        // This module's share of the deploy-time migration run, registered beside
        // the context it migrates so the two cannot drift apart. Deliberately not
        // in the narrow method: migrating is the whole module's business, and a
        // host that wanted only to read a timezone should not enlist in it.
        builder.Services.AddModuleMigrator<IdentityModuleDbContext>();

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
        builder.Services.AddScoped<DeleteAccountHandler>();
        builder.Services.AddScoped<ExportAccountHandler>();

        // Identity's own reaction to an erasure request: delete its user rows.
        // Other modules register their own handler for the same event.
        builder.Services.AddEventHandler<UserDataDeletionRequested, AccountErasureHandler>();

        // And its share of an export, which is the same fan-out read the other way
        // round. Registered as one of many: the handler that composes the document
        // takes every registered exporter and knows none of them by name.
        builder.Services.AddScoped<IUserDataExporter, IdentityDataExporter>();

        // What this module owes the rest of the system, delivered durably. Erasure
        // is a promise made synchronously and kept afterwards: lose the request
        // between the 204 and the dispatch and nothing is erased at all, with the
        // user already told otherwise. At-least-once delivery over handlers that
        // are already idempotent is exactly the shape that promise needs.
        builder.AddOutboxDispatcher<IdentityModuleDbContext>(RegisterOutboxEvents);

        // The caller of the current request, implemented internally. Its sibling on
        // the Contracts surface - the profile read - is registered by the narrow
        // method above, because a host can want that one on its own.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IUserContext, HttpContextUserContext>();

        return builder;
    }

    /// <summary>
    /// Registers the account store and <see cref="IUserProfileQuery"/> over it, and
    /// nothing else - no credentials, no token model, no endpoints, and above all no
    /// outbox dispatcher.
    /// <para>
    /// <b>This exists for the worker</b>, which schedules the follow-up scan. A
    /// follow-up fires at 11:00 in the owner's own timezone like every other
    /// clock-based reminder, that timezone lives in this module, and this contract is
    /// the one sanctioned way to read it. Composing the whole module there instead
    /// would hand the worker Identity's <b>outbox dispatcher</b>, which claims owed
    /// events and marks them processed once its handlers have run - and the worker
    /// registers none, so it would quietly consume every registration and account
    /// provisioning event and deliver them nowhere.
    /// </para>
    /// <para>
    /// Safe to call alone or through <see cref="AddIdentityModule"/>, which delegates
    /// to it; a host that calls both gets one context and one query.
    /// </para>
    /// </summary>
    public static IHostApplicationBuilder AddIdentityProfileQuery(this IHostApplicationBuilder builder)
    {
        if (builder.Services.Any(service => service.ServiceType == typeof(IUserProfileQuery)))
        {
            return builder;
        }

        var connectionString = builder.Configuration.GetConnectionString("jobspect")
            ?? throw new InvalidOperationException(
                "Connection string 'jobspect' is not configured. It is injected by the AppHost.");

        builder.Services.AddDbContext<IdentityModuleDbContext>(options =>
            NpgsqlContextConfiguration.Configure(options, connectionString, IdentityModuleDbContext.Schema));

        // Aspire adds health checks, a retrying execution strategy and telemetry
        // to the context registered above, without owning its configuration.
        builder.EnrichNpgsqlDbContext<IdentityModuleDbContext>();

        builder.Services.AddScoped<IUserProfileQuery, EfUserProfileQuery>();

        return builder;
    }

    /// <summary>
    /// Every event this module records for durable delivery, named in one place.
    /// A method rather than a lambda at the call site so the test that holds the
    /// module to publishing nothing unregistered can register exactly what the
    /// host registers, instead of a copy that would drift from it.
    /// </summary>
    internal static void RegisterOutboxEvents(OutboxEventRegistry registry) =>
        registry
            .Register<UserRegistered>()
            .Register<UserDataDeletionRequested>();

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
        DeleteAccountEndpoint.Map(account);
        ExportAccountEndpoint.Map(account);

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
