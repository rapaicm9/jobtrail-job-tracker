using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// The token payload as a client sees it - declared here on purpose, not
/// shared with the module, so a contract change breaks these tests instead of
/// silently retargeting them.
/// </summary>
internal sealed record AuthTokens(
    Guid UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// The account profile as a client sees it - declared here, not shared with the
/// module, so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record AccountProfile(
    Guid UserId,
    string Email,
    string TimeZoneId,
    DateTimeOffset CreatedAt);

/// <summary>
/// The plan status as a client sees it - declared here, not shared with the
/// module, so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record PlanStatus(string Tier, DateTimeOffset? UpdatedAt);

/// <summary>
/// A company picker row as a client sees it - declared here, not shared with the
/// module, so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record CompanySummary(Guid Id, string Name);

/// <summary>A compensation amount and currency, as a client sees it.</summary>
internal sealed record MoneyView(decimal Amount, string Currency);

/// <summary>
/// The dashboard's Free figures as a client sees them - declared here, not shared
/// with the module, so a contract change breaks these tests rather than silently
/// retargeting them.
/// </summary>
internal sealed record AnalyticsOverview(int TotalApplied, IReadOnlyList<PipelineStageCountView> Pipeline);

/// <summary>One column of the pipeline snapshot, as a client sees it.</summary>
internal sealed record PipelineStageCountView(string Stage, int Count);

/// <summary>
/// The custom-field answers as a client sees them, keyed by definition id.
/// <para>
/// A dictionary with value equality, because the views that carry it are records
/// and several tests compare one whole: a plain dictionary compares by reference,
/// so two responses holding identical answers would never be equal. Values are
/// compared as the JSON they arrived as - <see cref="JsonElement"/> has no
/// equality of its own, and the raw text is exactly what was stored.
/// </para>
/// </summary>
internal sealed class CustomFieldBag : Dictionary<Guid, JsonElement>, IEquatable<CustomFieldBag>
{
    public bool Equals(CustomFieldBag? other) =>
        other is not null
        && Count == other.Count
        && this.All(entry =>
            other.TryGetValue(entry.Key, out var value)
            && value.GetRawText() == entry.Value.GetRawText());

    public override bool Equals(object? obj) => Equals(obj as CustomFieldBag);

    public override int GetHashCode() => Count;
}

/// <summary>
/// A custom-field definition as a client sees it - declared here, not shared with
/// the module, so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record CustomFieldView(
    Guid Id,
    string Label,
    string Type,
    IReadOnlyList<string> Options,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// A campaign as a client sees it - declared here, not shared with the module, so a
/// contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record CampaignView(
    Guid Id,
    string Name,
    bool IsDefault,
    int ApplicationCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// One page of a list, as a client sees it - declared here, not shared with the
/// module, so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record PageView<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// An interview round as a client sees it - declared here, not shared with the
/// module, so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record InterviewView(
    Guid Id,
    Guid ApplicationId,
    DateTimeOffset ScheduledAt,
    string Type,
    string Format,
    string Outcome,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// A timeline entry as a client sees it - declared here, not shared with the
/// module, so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record ActivityEntryView(
    Guid Id,
    string Kind,
    DateTimeOffset OccurredAt,
    string? FromStage,
    string? ToStage,
    string? TransitionKind,
    string? Note);

/// <summary>
/// A contact as a client sees it - declared here, not shared with the module, so a
/// contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record ContactView(
    Guid Id,
    Guid? ApplicationId,
    Guid? CompanyId,
    string Name,
    string? Role,
    string? Email,
    string? Phone,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// An application list row as a client sees it - declared here, not shared with
/// the module, so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record ApplicationSummaryView(
    Guid Id,
    Guid CampaignId,
    Guid? CompanyId,
    string Stage,
    string Role,
    string? WorkMode,
    DateOnly AppliedDate,
    DateOnly? ApplicationDeadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// An application as a client sees it - declared here, not shared with the module,
/// so a contract change breaks these tests rather than retargeting them.
/// </summary>
internal sealed record ApplicationView(
    Guid Id,
    Guid CampaignId,
    Guid? CompanyId,
    string Stage,
    string Role,
    MoneyView? Compensation,
    string? Location,
    string? WorkMode,
    string? PostingUrl,
    string? Source,
    DateOnly AppliedDate,
    DateOnly? ApplicationDeadline,
    DateOnly? OfferDecisionDeadline,
    string? CvLabel,
    string? CoverLetterLabel,
    CustomFieldBag CustomFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class ApiClient
{
    public const string Password = "Correct-horse7";

    /// <summary>A fresh address per test: data isolation without respawning the DB.</summary>
    public static string UniqueEmail() => $"{Guid.CreateVersion7():N}@example.com";

    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client, string email, string password = Password,
        string? timeZoneId = null, string? deviceLabel = null) =>
        client.PostAsJsonAsync("/api/v1/identity/register", new { email, password, timeZoneId, deviceLabel });

    public static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string email, string password = Password, string? deviceLabel = null) =>
        client.PostAsJsonAsync("/api/v1/identity/login", new { email, password, deviceLabel });

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/identity/refresh", new { refreshToken });

    public static Task<HttpResponseMessage> LogoutAsync(this HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/identity/logout", new { refreshToken });

    public static Task<HttpResponseMessage> LogoutAllAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/identity/logout-all", accessToken));

    public static Task<HttpResponseMessage> GetAccountAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/account", accessToken));

    public static Task<HttpResponseMessage> ExportAccountAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/account/export", accessToken));

    public static Task<HttpResponseMessage> UpdateAccountAsync(
        this HttpClient client, string? accessToken, string? timeZoneId)
    {
        var request = Authorized(HttpMethod.Put, "/api/v1/account", accessToken);
        request.Content = JsonContent.Create(new { timeZoneId });
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> DeleteAccountAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Delete, "/api/v1/account", accessToken));

    public static Task<HttpResponseMessage> GetPlanAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/billing/plan", accessToken));

    public static Task<HttpResponseMessage> PurchaseProAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/billing/purchase", accessToken));

    public static Task<HttpResponseMessage> GrantProAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/billing/dev/grant-pro", accessToken));

    public static Task<HttpResponseMessage> SearchCompaniesAsync(
        this HttpClient client, string? accessToken, string? query) =>
        client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/companies?query={Uri.EscapeDataString(query ?? string.Empty)}",
            accessToken));

    public static Task<HttpResponseMessage> CreateApplicationAsync(
        this HttpClient client, string? accessToken, object body)
    {
        var request = Authorized(HttpMethod.Post, "/api/v1/applications", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetApplicationAsync(
        this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/applications/{id}", accessToken));

    public static Task<HttpResponseMessage> ListApplicationsAsync(
        this HttpClient client,
        string? accessToken,
        int? limit = null,
        string? cursor = null,
        Guid? campaignId = null,
        Guid? customFieldId = null,
        string? customFieldValue = null,
        Guid? sortCustomFieldId = null,
        string? sortDirection = null)
    {
        var query = new List<string>();
        if (campaignId is { } campaign)
        {
            query.Add($"campaignId={campaign}");
        }

        if (customFieldId is { } fieldId)
        {
            query.Add($"customFieldId={fieldId}");
        }

        if (customFieldValue is not null)
        {
            query.Add($"customFieldValue={Uri.EscapeDataString(customFieldValue)}");
        }

        if (sortCustomFieldId is { } sortFieldId)
        {
            query.Add($"sortCustomFieldId={sortFieldId}");
        }

        if (sortDirection is not null)
        {
            query.Add($"sortDirection={Uri.EscapeDataString(sortDirection)}");
        }

        if (limit is { } requested)
        {
            query.Add($"limit={requested}");
        }

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var uri = "/api/v1/applications" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        return client.SendAsync(Authorized(HttpMethod.Get, uri, accessToken));
    }

    public static Task<HttpResponseMessage> UpdateApplicationAsync(
        this HttpClient client, string? accessToken, Guid id, object body)
    {
        var request = Authorized(HttpMethod.Put, $"/api/v1/applications/{id}", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> CreateContactAsync(this HttpClient client, string? accessToken, object body)
    {
        var request = Authorized(HttpMethod.Post, "/api/v1/contacts", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetContactAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/contacts/{id}", accessToken));

    public static Task<HttpResponseMessage> ListContactsAsync(
        this HttpClient client, string? accessToken, Guid? applicationId = null, Guid? companyId = null,
        int? limit = null, string? cursor = null)
    {
        var query = new List<string>();
        if (applicationId is { } appId)
        {
            query.Add($"applicationId={appId}");
        }

        if (companyId is { } companyIdValue)
        {
            query.Add($"companyId={companyIdValue}");
        }

        if (limit is { } requested)
        {
            query.Add($"limit={requested}");
        }

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var uri = "/api/v1/contacts" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        return client.SendAsync(Authorized(HttpMethod.Get, uri, accessToken));
    }

    public static Task<HttpResponseMessage> UpdateContactAsync(
        this HttpClient client, string? accessToken, Guid id, object body)
    {
        var request = Authorized(HttpMethod.Put, $"/api/v1/contacts/{id}", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> CreateInterviewAsync(
        this HttpClient client, string? accessToken, Guid applicationId, object body)
    {
        var request = Authorized(HttpMethod.Post, $"/api/v1/applications/{applicationId}/interviews", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetInterviewAsync(
        this HttpClient client, string? accessToken, Guid applicationId, Guid interviewId) =>
        client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/applications/{applicationId}/interviews/{interviewId}", accessToken));

    public static Task<HttpResponseMessage> ListInterviewsAsync(
        this HttpClient client, string? accessToken, Guid applicationId, int? limit = null, string? cursor = null) =>
        client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/applications/{applicationId}/interviews" + Paging(limit, cursor),
            accessToken));

    public static Task<HttpResponseMessage> UpdateInterviewAsync(
        this HttpClient client, string? accessToken, Guid applicationId, Guid interviewId, object body)
    {
        var request = Authorized(
            HttpMethod.Put, $"/api/v1/applications/{applicationId}/interviews/{interviewId}", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> CreateCustomFieldAsync(
        this HttpClient client, string? accessToken, object body)
    {
        var request = Authorized(HttpMethod.Post, "/api/v1/custom-fields", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetCustomFieldAsync(
        this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/custom-fields/{id}", accessToken));

    public static Task<HttpResponseMessage> ListCustomFieldsAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/custom-fields", accessToken));

    public static Task<HttpResponseMessage> UpdateCustomFieldAsync(
        this HttpClient client, string? accessToken, Guid id, object body)
    {
        var request = Authorized(HttpMethod.Put, $"/api/v1/custom-fields/{id}", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> CreateCampaignAsync(
        this HttpClient client, string? accessToken, object body)
    {
        var request = Authorized(HttpMethod.Post, "/api/v1/campaigns", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetCampaignAsync(
        this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/campaigns/{id}", accessToken));

    public static Task<HttpResponseMessage> ListCampaignsAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/campaigns", accessToken));

    public static Task<HttpResponseMessage> UpdateCampaignAsync(
        this HttpClient client, string? accessToken, Guid id, object body)
    {
        var request = Authorized(HttpMethod.Put, $"/api/v1/campaigns/{id}", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> DeleteCampaignAsync(
        this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(Authorized(HttpMethod.Delete, $"/api/v1/campaigns/{id}", accessToken));

    public static Task<HttpResponseMessage> GetAnalyticsOverviewAsync(
        this HttpClient client, string? accessToken, Guid? campaignId = null) =>
        client.SendAsync(Authorized(
            HttpMethod.Get,
            campaignId is { } id
                ? $"/api/v1/analytics/overview?campaignId={id}"
                : "/api/v1/analytics/overview",
            accessToken));

    public static Task<HttpResponseMessage> AddNoteAsync(
        this HttpClient client, string? accessToken, Guid applicationId, object body)
    {
        var request = Authorized(HttpMethod.Post, $"/api/v1/applications/{applicationId}/activity", accessToken);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetActivityAsync(
        this HttpClient client, string? accessToken, Guid applicationId, int? limit = null, string? cursor = null) =>
        client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/applications/{applicationId}/activity" + Paging(limit, cursor),
            accessToken));

    /// <summary>The paging query string, or nothing when neither parameter is being exercised.</summary>
    private static string Paging(int? limit, string? cursor)
    {
        var query = new List<string>();
        if (limit is { } requested)
        {
            query.Add($"limit={requested}");
        }

        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        return query.Count > 0 ? "?" + string.Join('&', query) : string.Empty;
    }

    public static Task<HttpResponseMessage> TransitionApplicationAsync(
        this HttpClient client, string? accessToken, Guid id, string? targetStage)
    {
        var request = Authorized(HttpMethod.Post, $"/api/v1/applications/{id}/transition", accessToken);
        request.Content = JsonContent.Create(new { targetStage });
        return client.SendAsync(request);
    }

    /// <summary>
    /// A request with an <c>Idempotency-Key</c>, for the tests whose subject is
    /// the header rather than any one route. The route is written out at the call
    /// site on purpose: these assert the wire contract, so spelling it is the point.
    /// </summary>
    public static Task<HttpResponseMessage> SendWithKeyAsync(
        this HttpClient client,
        HttpMethod method,
        string uri,
        string? accessToken,
        object? body = null,
        string? idempotencyKey = null) =>
        client.SendContentWithKeyAsync(
            method, uri, accessToken, body is null ? null : JsonContent.Create(body), idempotencyKey);

    /// <summary>
    /// The same, over content the caller built. Used where a test needs the exact
    /// bytes on the wire - the request fingerprint is computed over them, so a
    /// serializer's choices must not come between the test and the server.
    /// </summary>
    public static Task<HttpResponseMessage> SendContentWithKeyAsync(
        this HttpClient client,
        HttpMethod method,
        string uri,
        string? accessToken,
        HttpContent? content = null,
        string? idempotencyKey = null)
    {
        var request = Authorized(method, uri, accessToken);
        request.Content = content;

        if (idempotencyKey is not null)
        {
            // Unvalidated: these tests deliberately send keys the client would
            // otherwise refuse to put on the wire, and the server is what decides.
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string uri, string? accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return request;
    }

    /// <summary>Registers a fresh account and hands back its token pair.</summary>
    public static async Task<AuthTokens> RegisterNewUserAsync(this HttpClient client)
    {
        var response = await client.RegisterAsync(UniqueEmail());
        return await response.ReadTokensAsync();
    }

    public static async Task<AuthTokens> ReadTokensAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>();
        return tokens.ShouldNotBeNull();
    }

    public static async Task<AccountProfile> ReadProfileAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var profile = await response.Content.ReadFromJsonAsync<AccountProfile>();
        return profile.ShouldNotBeNull();
    }

    public static async Task<PlanStatus> ReadPlanAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var plan = await response.Content.ReadFromJsonAsync<PlanStatus>();
        return plan.ShouldNotBeNull();
    }

    public static async Task<IReadOnlyList<CompanySummary>> ReadCompaniesAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var companies = await response.Content.ReadFromJsonAsync<List<CompanySummary>>();
        return companies.ShouldNotBeNull();
    }

    public static async Task<ApplicationView> ReadApplicationAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var application = await response.Content.ReadFromJsonAsync<ApplicationView>();
        return application.ShouldNotBeNull();
    }

    /// <summary>
    /// The rows of a page, for the tests that care about content rather than
    /// paging; <see cref="ReadPageAsync{T}"/> is the one that also sees the cursor.
    /// </summary>
    public static async Task<IReadOnlyList<ApplicationSummaryView>> ReadApplicationListAsync(
        this HttpResponseMessage response) =>
        (await response.ReadPageAsync<ApplicationSummaryView>()).Items;

    public static async Task<ContactView> ReadContactAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var contact = await response.Content.ReadFromJsonAsync<ContactView>();
        return contact.ShouldNotBeNull();
    }

    public static async Task<IReadOnlyList<ContactView>> ReadContactListAsync(this HttpResponseMessage response) =>
        (await response.ReadPageAsync<ContactView>()).Items;

    public static async Task<ActivityEntryView> ReadActivityEntryAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var entry = await response.Content.ReadFromJsonAsync<ActivityEntryView>();
        return entry.ShouldNotBeNull();
    }

    public static async Task<IReadOnlyList<ActivityEntryView>> ReadActivityAsync(this HttpResponseMessage response) =>
        (await response.ReadPageAsync<ActivityEntryView>()).Items;

    public static async Task<CustomFieldView> ReadCustomFieldAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var definition = await response.Content.ReadFromJsonAsync<CustomFieldView>();
        return definition.ShouldNotBeNull();
    }

    /// <summary>The custom-field list is a bare array, not the paged envelope.</summary>
    public static async Task<IReadOnlyList<CustomFieldView>> ReadCustomFieldListAsync(
        this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var definitions = await response.Content.ReadFromJsonAsync<List<CustomFieldView>>();
        return definitions.ShouldNotBeNull();
    }

    public static async Task<CampaignView> ReadCampaignAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var campaign = await response.Content.ReadFromJsonAsync<CampaignView>();
        return campaign.ShouldNotBeNull();
    }

    public static async Task<AnalyticsOverview> ReadOverviewAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var overview = await response.Content.ReadFromJsonAsync<AnalyticsOverview>();
        return overview.ShouldNotBeNull();
    }

    /// <summary>The campaign list is a bare array, not the paged envelope.</summary>
    public static async Task<IReadOnlyList<CampaignView>> ReadCampaignListAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var campaigns = await response.Content.ReadFromJsonAsync<List<CampaignView>>();
        return campaigns.ShouldNotBeNull();
    }

    public static async Task<InterviewView> ReadInterviewAsync(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var interview = await response.Content.ReadFromJsonAsync<InterviewView>();
        return interview.ShouldNotBeNull();
    }

    public static async Task<IReadOnlyList<InterviewView>> ReadInterviewListAsync(
        this HttpResponseMessage response) =>
        (await response.ReadPageAsync<InterviewView>()).Items;

    /// <summary>One page of any list, cursor included.</summary>
    public static async Task<PageView<T>> ReadPageAsync<T>(this HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");
        var page = await response.Content.ReadFromJsonAsync<PageView<T>>();
        return page.ShouldNotBeNull();
    }
}
