using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Features.EraseData;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The Applications module's share of the erasure fan-out - the largest in the
/// system, and the one holding the user's own words. The claims: an erasure takes
/// every table in the module's schema, including the events still owed on the
/// user's behalf; it touches nobody else's rows; and it reaches this module when
/// an account is deleted for real.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApplicationsErasureTests(ApiFixture fixture)
{
    /// <summary>
    /// Every table the module holds a user in, spelled out rather than derived.
    /// The test below derives the same list from the database and compares: if a
    /// later slice adds an owner-scoped table, the two disagree and this test says
    /// so, which is the point - a new table is a new thing to erase.
    /// </summary>
    private static readonly string[] OwnedTables =
    [
        "activity_log", "applications", "campaigns", "companies", "contacts", "custom_fields", "interviews",
        "outbox",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Leaves_no_table_in_the_schema_holding_the_user()
    {
        var tokens = await SeedAsync();
        var ownerId = UserId.From(tokens.UserId);

        // The seed has to reach every table, or an erasure proving nothing would
        // still pass. This is the assertion that keeps the one below honest.
        var seeded = await OwnerRowCountsAsync(ownerId);
        seeded.Where(table => table.Rows > 0).Select(table => table.Table).ShouldBe(OwnedTables, ignoreOrder: true);

        await EraseAsync(ownerId);

        // Asked of the database, not of the handler: any owner-scoped table the
        // module grows later is included here without anyone remembering to add it.
        (await OwnerRowCountsAsync(ownerId)).ShouldAllBe(table => table.Rows == 0);
    }

    [Fact]
    public async Task Takes_the_events_still_owed_on_the_users_behalf()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);
        await (await _client.CreateApplicationAsync(tokens.AccessToken, new { role = "Engineer" }))
            .ReadApplicationAsync();

        await EraseAsync(ownerId);

        // Delivered or not, an owed event is data held about this user - and an
        // undelivered one would reach consumers after they had erased the same
        // user, putting the data back.
        (await fixture.MessagesForAsync(tokens.UserId, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaves_another_users_data_alone()
    {
        var mine = await SeedAsync();
        var theirs = await SeedAsync();

        await EraseAsync(UserId.From(mine.UserId));

        var untouched = await OwnerRowCountsAsync(UserId.From(theirs.UserId));
        untouched.Where(table => table.Rows > 0).Select(table => table.Table).ShouldBe(OwnedTables, ignoreOrder: true);
    }

    [Fact]
    public async Task Erasing_a_user_this_module_never_held_does_nothing()
    {
        // Idempotent, as the bus requires: it can deliver to a user already erased,
        // or to one who never opened an application.
        await Should.NotThrowAsync(EraseAsync(UserId.New()));
    }

    [Fact]
    public async Task Erasing_twice_is_the_same_as_erasing_once()
    {
        var tokens = await SeedAsync();
        var ownerId = UserId.From(tokens.UserId);

        await EraseAsync(ownerId);
        await Should.NotThrowAsync(EraseAsync(ownerId));

        (await OwnerRowCountsAsync(ownerId)).ShouldAllBe(table => table.Rows == 0);
    }

    [Fact]
    public async Task Deleting_the_account_erases_the_users_applications()
    {
        var tokens = await SeedAsync();
        var ownerId = UserId.From(tokens.UserId);

        (await _client.DeleteAccountAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The deletion fans out through the bus and reaches this module shortly
        // after - the path a real erasure takes, end to end.
        await Poll.UntilAsync(
            async () => (await OwnerRowCountsAsync(ownerId)).All(table => table.Rows == 0),
            "deleting the account should erase the user's application data",
            Ct);
    }

    /// <summary>
    /// A user with something in every table: a campaign from registration, then an
    /// application that creates a company and opens a timeline, a contact, an
    /// interview, a note, and a move that records both an entry and its events.
    /// </summary>
    private async Task<AuthTokens> SeedAsync()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // Defining a field needs the entitlement, so the seed buys it. Without a
        // custom field the row-count check below would pass while proving nothing
        // about the newest table in the schema.
        await fixture.UnlockProAsync(_client, tokens, Ct);
        await ShouldSucceedAsync(_client.CreateCustomFieldAsync(
            tokens.AccessToken, new { label = "Referral source", type = "text" }));

        var application = await (await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role = "Staff Engineer",
            companyName = "Acme Corp",
            applicationDeadline = "2026-09-01",
        })).ReadApplicationAsync();

        await ShouldSucceedAsync(_client.CreateContactAsync(
            tokens.AccessToken, new { applicationId = application.Id, name = "Alice Recruiter" }));

        await ShouldSucceedAsync(_client.CreateInterviewAsync(tokens.AccessToken, application.Id, new
        {
            scheduledAt = "2026-08-01T14:00:00Z",
            type = "technical",
            format = "remote",
        }));

        await ShouldSucceedAsync(_client.AddNoteAsync(
            tokens.AccessToken, application.Id, new { note = "They want a second round." }));

        await ShouldSucceedAsync(
            _client.TransitionApplicationAsync(tokens.AccessToken, application.Id, "Screening"));

        return tokens;
    }

    private static async Task ShouldSucceedAsync(Task<HttpResponseMessage> request) =>
        (await request).IsSuccessStatusCode.ShouldBeTrue();

    private async Task EraseAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        await new ApplicationsDataErasureHandler(dbContext)
            .HandleAsync(new UserDataDeletionRequested(Guid.CreateVersion7(), ownerId), Ct);
    }

    /// <summary>
    /// How many rows each owner-scoped table in the module's schema holds for this
    /// user. The tables come from the database's own catalogue rather than from a
    /// list here, so nothing has to remember to keep the two in step.
    /// </summary>
    private async Task<IReadOnlyList<(string Table, long Rows)>> OwnerRowCountsAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        var tables = await dbContext.Database.SqlQueryRaw<string>(
            """
            SELECT table_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'applications' AND column_name = 'owner_id'
            ORDER BY table_name
            """).ToListAsync(Ct);

        var counts = new List<(string, long)>(tables.Count);

        foreach (var table in tables)
        {
            // The name came out of the catalogue a line ago rather than from
            // anything a caller supplied, and is held to the shape a table name can
            // take before being spliced in. The analyzer can see neither fact.
            if (!table.All(character => character is (>= 'a' and <= 'z') or '_'))
            {
                throw new InvalidOperationException($"Unexpected table name in the applications schema: {table}.");
            }

#pragma warning disable EF1003 // A validated catalogue name; the owner id is parameterized.
            var rows = await dbContext.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM applications." + table + " WHERE owner_id = {0}",
                ownerId.Value).SingleAsync(Ct);
#pragma warning restore EF1003

            counts.Add((table, rows));
        }

        return counts;
    }
}
