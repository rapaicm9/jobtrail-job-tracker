using Jobspect.Infrastructure.Events;
using Jobspect.Infrastructure.Outbox;
using Jobspect.Infrastructure.Persistence;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Applications.Features.AddNote;
using Jobspect.Modules.Applications.Features.ChartCustomField;
using Jobspect.Modules.Applications.Features.CreateApplication;
using Jobspect.Modules.Applications.Features.CreateCampaign;
using Jobspect.Modules.Applications.Features.CreateContact;
using Jobspect.Modules.Applications.Features.CreateCustomField;
using Jobspect.Modules.Applications.Features.CreateInterview;
using Jobspect.Modules.Applications.Features.DeleteCampaign;
using Jobspect.Modules.Applications.Features.EraseData;
using Jobspect.Modules.Applications.Features.ExportData;
using Jobspect.Modules.Applications.Features.GetActivity;
using Jobspect.Modules.Applications.Features.GetApplication;
using Jobspect.Modules.Applications.Features.GetCampaign;
using Jobspect.Modules.Applications.Features.GetContact;
using Jobspect.Modules.Applications.Features.GetCustomField;
using Jobspect.Modules.Applications.Features.GetInterview;
using Jobspect.Modules.Applications.Features.ListApplications;
using Jobspect.Modules.Applications.Features.ListCampaigns;
using Jobspect.Modules.Applications.Features.ListContacts;
using Jobspect.Modules.Applications.Features.ListCustomFields;
using Jobspect.Modules.Applications.Features.ListInterviews;
using Jobspect.Modules.Applications.Features.ProvisionCampaign;
using Jobspect.Modules.Applications.Features.SearchCompanies;
using Jobspect.Modules.Applications.Features.TransitionApplication;
using Jobspect.Modules.Applications.Features.UpdateApplication;
using Jobspect.Modules.Applications.Features.UpdateCampaign;
using Jobspect.Modules.Applications.Features.UpdateContact;
using Jobspect.Modules.Applications.Features.UpdateCustomField;
using Jobspect.Modules.Applications.Features.UpdateInterview;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Jobspect.Modules.Applications;

/// <summary>
/// The Applications module's composition surface. A host calls
/// <see cref="AddApplicationsModule"/> to register the application store;
/// everything the module owns stays internal behind it. The aggregate's
/// behaviour, endpoints and event reactions arrive in later slices.
/// </summary>
public static class ApplicationsModule
{
    public static IHostApplicationBuilder AddApplicationsModule(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("jobspect")
            ?? throw new InvalidOperationException(
                "Connection string 'jobspect' is not configured. It is injected by the AppHost.");

        builder.Services.AddDbContext<ApplicationsDbContext>(options =>
            NpgsqlContextConfiguration.Configure(options, connectionString, ApplicationsDbContext.Schema));

        // Aspire adds health checks, a retrying execution strategy and telemetry
        // to the context registered above, without owning its configuration.
        builder.EnrichNpgsqlDbContext<ApplicationsDbContext>();

        // This module's share of the deploy-time migration run, registered beside
        // the context it migrates so the two cannot drift apart.
        builder.Services.AddModuleMigrator<ApplicationsDbContext>();

        // Every new account gets its default campaign, off Identity's UserRegistered.
        builder.Services.AddEventHandler<UserRegistered, CampaignProvisioningHandler>();

        // And gives everything back on the way out: this module's share of the
        // erasure fan-out, from its own schema only.
        builder.Services.AddEventHandler<UserDataDeletionRequested, ApplicationsDataErasureHandler>();

        // And hands it all over on request: this module's share of an account
        // export, which is the largest of them - the whole record of a job search.
        builder.Services.AddScoped<IUserDataExporter, ApplicationsDataExporter>();

        // The clock every handler here dates its writes by. Registered defensively:
        // the module should stand up whether or not another one got here first.
        builder.Services.TryAddSingleton(TimeProvider.System);

        // What this module owes other modules, delivered durably. A consumer that
        // misses one of these cannot catch up by reading our tables, so the event
        // is recorded with the change and delivered until its handlers succeed.
        builder.AddOutboxDispatcher<ApplicationsDbContext>(RegisterOutboxEvents);

        // This module's first published query. Analytics charts the custom fields
        // by asking for figures rather than reading the bag, because no event
        // carries an answer and none ever will.
        builder.Services.AddScoped<ICustomFieldChartQuery, CustomFieldChartQuery>();

        builder.Services.AddScoped<SearchCompaniesHandler>();
        builder.Services.AddScoped<CompanyResolver>();
        builder.Services.AddScoped<CustomFieldValueResolver>();
        builder.Services.AddScoped<CustomFieldFilterResolver>();
        builder.Services.AddScoped<CustomFieldSortResolver>();
        builder.Services.AddScoped<OwnershipGuard>();
        builder.Services.AddScoped<CreateApplicationHandler>();
        builder.Services.AddScoped<GetApplicationHandler>();
        builder.Services.AddScoped<ListApplicationsHandler>();
        builder.Services.AddScoped<UpdateApplicationHandler>();
        builder.Services.AddScoped<TransitionApplicationHandler>();

        builder.Services.AddScoped<ContactLinkGuard>();
        builder.Services.AddScoped<CreateContactHandler>();
        builder.Services.AddScoped<GetContactHandler>();
        builder.Services.AddScoped<ListContactsHandler>();
        builder.Services.AddScoped<UpdateContactHandler>();

        builder.Services.AddScoped<CreateInterviewHandler>();
        builder.Services.AddScoped<GetInterviewHandler>();
        builder.Services.AddScoped<ListInterviewsHandler>();
        builder.Services.AddScoped<UpdateInterviewHandler>();

        builder.Services.AddScoped<AddNoteHandler>();
        builder.Services.AddScoped<GetActivityHandler>();

        builder.Services.AddScoped<CreateCustomFieldHandler>();
        builder.Services.AddScoped<GetCustomFieldHandler>();
        builder.Services.AddScoped<ListCustomFieldsHandler>();
        builder.Services.AddScoped<UpdateCustomFieldHandler>();

        builder.Services.AddScoped<CreateCampaignHandler>();
        builder.Services.AddScoped<GetCampaignHandler>();
        builder.Services.AddScoped<ListCampaignsHandler>();
        builder.Services.AddScoped<UpdateCampaignHandler>();
        builder.Services.AddScoped<DeleteCampaignHandler>();

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
            .Register<ApplicationSubmitted>()
            .Register<ApplicationStageChanged>()
            .Register<ApplicationReachedTerminal>()
            .Register<ApplicationReopened>()
            .Register<ApplicationMovedToCampaign>()
            .Register<ApplicationDeadlineSet>()
            .Register<OfferDecisionDeadlineSet>()
            .Register<InterviewScheduled>()
            .Register<InterviewCancelled>();

    /// <summary>
    /// Maps the Applications module's authenticated slices onto the host's
    /// versioned API group. The module owns several top-level resources, so this
    /// mounts each on its own group - the top-level <c>/companies</c>,
    /// <c>/applications</c> and <c>/contacts</c>, plus the interviews and timeline
    /// nested under an application, since neither has a life apart from it. It is
    /// the module's endpoints, not one route. Takes the host's general per-IP budget.
    /// </summary>
    public static void MapApplicationsEndpoints(this IEndpointRouteBuilder api)
    {
        var companies = api.MapGroup("/companies");
        SearchCompaniesEndpoint.Map(companies);

        var applications = api.MapGroup("/applications");
        ListApplicationsEndpoint.Map(applications);
        CreateApplicationEndpoint.Map(applications);
        GetApplicationEndpoint.Map(applications);
        UpdateApplicationEndpoint.Map(applications);
        TransitionApplicationEndpoint.Map(applications);

        var contacts = api.MapGroup("/contacts");
        ListContactsEndpoint.Map(contacts);
        CreateContactEndpoint.Map(contacts);
        GetContactEndpoint.Map(contacts);
        UpdateContactEndpoint.Map(contacts);

        // Interviews live under their application - a round has no life apart from it.
        var interviews = api.MapGroup("/applications/{applicationId:guid}/interviews");
        ListInterviewsEndpoint.Map(interviews);
        CreateInterviewEndpoint.Map(interviews);
        GetInterviewEndpoint.Map(interviews);
        UpdateInterviewEndpoint.Map(interviews);

        // The application's timeline, read whole and appended to a note at a time.
        var activity = api.MapGroup("/applications/{applicationId:guid}/activity");
        GetActivityEndpoint.Map(activity);
        AddNoteEndpoint.Map(activity);

        // The user's own fields. Account-level rather than nested under an
        // application - one definition answered by all of them. The Pro gate sits
        // on the two write endpoints rather than on this group, so an account that
        // cannot define a field can still read the ones it has.
        var customFields = api.MapGroup("/custom-fields");
        ListCustomFieldsEndpoint.Map(customFields);
        CreateCustomFieldEndpoint.Map(customFields);
        GetCustomFieldEndpoint.Map(customFields);
        UpdateCustomFieldEndpoint.Map(customFields);

        // The account's job searches. Top-level rather than nested: an application
        // belongs to a campaign, not the other way round, and the picker that offers
        // them is drawn before any application exists. Only the create carries the
        // Pro gate - holding more than one is the capability; reading, renaming and
        // deleting act on campaigns the account already has.
        var campaigns = api.MapGroup("/campaigns");
        ListCampaignsEndpoint.Map(campaigns);
        CreateCampaignEndpoint.Map(campaigns);
        GetCampaignEndpoint.Map(campaigns);
        UpdateCampaignEndpoint.Map(campaigns);
        DeleteCampaignEndpoint.Map(campaigns);
    }
}
