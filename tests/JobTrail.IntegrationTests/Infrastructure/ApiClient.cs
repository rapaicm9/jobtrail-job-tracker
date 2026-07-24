using System.Net.Http.Headers;
using System.Net.Http.Json;
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
}
