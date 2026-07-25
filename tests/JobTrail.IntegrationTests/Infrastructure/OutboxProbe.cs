using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Applications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// Reads the Applications module's outbox back. The rows are the module's own
/// bookkeeping rather than an API surface, so a test that cares what was announced
/// has to look at them directly.
/// <para>
/// Rows are matched on an id inside the payload: the suite shares one database, so
/// the application (or interview) a test is working on is what separates its events
/// from every other test's. They come back in the order the dispatcher would claim
/// them - which for rows written in one transaction is not a meaningful order, so a
/// test asserting on a set should say so rather than lean on this.
/// </para>
/// </summary>
internal static class OutboxProbe
{
    public static async Task<IReadOnlyList<OutboxMessage>> MessagesForAsync(
        this ApiFixture fixture, Guid subjectId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        var messages = await dbContext.Outbox
            .AsNoTracking()
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);

        return [.. messages.Where(message => message.Payload.Contains(subjectId.ToString(), StringComparison.Ordinal))];
    }

    /// <summary>What was announced about this subject, for asserting on the set of events a change produced.</summary>
    public static async Task<IReadOnlyList<string>> EventTypesForAsync(
        this ApiFixture fixture, Guid subjectId, CancellationToken cancellationToken) =>
        [.. (await fixture.MessagesForAsync(subjectId, cancellationToken)).Select(message => message.EventType)];

    /// <summary>The one row of a given event type for this subject; fails if there are none or several.</summary>
    public static async Task<OutboxMessage> SingleMessageForAsync(
        this ApiFixture fixture, Guid subjectId, string eventType, CancellationToken cancellationToken) =>
        (await fixture.MessagesForAsync(subjectId, cancellationToken))
            .Where(message => message.EventType == eventType)
            .ShouldHaveSingleItem();
}
