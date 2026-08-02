using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Identity.Features.DeleteAccount;
using Jobspect.Modules.Identity.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// Identity's own share of the erasure fan-out: the account row, and everything
/// the database cascades off it. Against a real PostgreSQL rather than a fake
/// store, because the cascade <em>is</em> the behaviour - a fake would happily
/// report the sessions gone without a foreign key having done anything.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IdentityErasureTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Erasure_removes_the_account_row()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        await EraseAsync(userId);

        (await fixture.UserExistsAsync(userId, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Erasure_takes_the_users_sessions_with_the_account()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        // Registration signs the user in, so there is a session to lose.
        (await fixture.RefreshTokenCountAsync(userId, Ct)).ShouldBeGreaterThan(0);

        await EraseAsync(userId);

        // Nothing here deletes these rows; the cascading foreign key does.
        (await fixture.RefreshTokenCountAsync(userId, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Erasing_an_account_that_is_already_gone_does_nothing()
    {
        // Idempotent, as at-least-once delivery requires: the same request can
        // arrive twice, or for a user this module never held.
        await Should.NotThrowAsync(EraseAsync(UserId.New()));
    }

    [Fact]
    public async Task Erasing_twice_is_the_same_as_erasing_once()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        await EraseAsync(userId);
        await Should.NotThrowAsync(EraseAsync(userId));

        (await fixture.UserExistsAsync(userId, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Leaves_another_users_account_alone()
    {
        var mine = await _client.RegisterNewUserAsync();
        var theirs = await _client.RegisterNewUserAsync();

        await EraseAsync(UserId.From(mine.UserId));

        (await fixture.UserExistsAsync(UserId.From(theirs.UserId), Ct)).ShouldBeTrue();
        (await fixture.RefreshTokenCountAsync(UserId.From(theirs.UserId), Ct)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Leaves_no_table_in_the_schema_holding_the_user()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        // The sweep has to see real data, or an erasure proving nothing would still
        // pass. Registration signs the user in, so refresh_tokens is populated.
        var seeded = await UserRowCountsAsync(userId);
        seeded.ShouldContain(table => table.Rows > 0);

        await EraseAsync(userId);

        // Asked of the database rather than of a list here: a table this module
        // grows later is swept without anyone remembering to add it, which is the
        // point - a new table hanging off the account is a new thing to erase, and
        // a missing cascade on it fails here rather than in production.
        (await UserRowCountsAsync(userId)).ShouldAllBe(table => table.Rows == 0);
    }

    private async Task EraseAsync(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        await new AccountErasureHandler(dbContext)
            .HandleAsync(new UserDataDeletionRequested(Guid.CreateVersion7(), userId), Ct);
    }

    /// <summary>
    /// How many rows each table hanging off the account holds for this user. The
    /// module names that column <c>user_id</c> throughout, so that is what the
    /// catalogue is asked for.
    /// <para>
    /// <c>outbox</c> is not among them, and not by accident: it names its owner
    /// <c>owner_id</c>, and it is deliberately outside erasure. That row is the
    /// record of the request being served, held under a lock by the dispatcher
    /// while the handlers run - deleting it from inside one of them would strand
    /// the delivery mid-flight. It leaves with the pruning, like every other.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<(string Table, long Rows)>> UserRowCountsAsync(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        var tables = await dbContext.Database.SqlQueryRaw<string>(
            """
            SELECT table_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'identity' AND column_name = 'user_id'
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
                throw new InvalidOperationException($"Unexpected table name in the identity schema: {table}.");
            }

#pragma warning disable EF1003 // A validated catalogue name; the user id is parameterized.
            var rows = await dbContext.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM identity." + table + " WHERE user_id = {0}",
                userId.Value).SingleAsync(Ct);
#pragma warning restore EF1003

            counts.Add((table, rows));
        }

        return counts;
    }
}
