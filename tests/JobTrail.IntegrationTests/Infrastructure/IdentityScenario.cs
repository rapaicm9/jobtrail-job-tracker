using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.Modules.Identity.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// Reads the Identity store directly, for the tests whose subject is what erasure
/// leaves behind rather than what an endpoint answers. Each call takes its own
/// scope, so a read never sees a write's still-tracked entity.
/// </summary>
internal static class IdentityScenario
{
    /// <summary>Whether the account row itself is still there.</summary>
    public static async Task<bool> UserExistsAsync(
        this ApiFixture fixture, UserId userId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        var id = userId.Value;
        return await db.Users.AsNoTracking().AnyAsync(user => user.Id == id, cancellationToken);
    }

    /// <summary>
    /// The user's sessions. They hang off the account by a cascading foreign key,
    /// so this is what proves the cascade actually ran rather than being assumed.
    /// </summary>
    public static async Task<int> RefreshTokenCountAsync(
        this ApiFixture fixture, UserId userId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        var id = userId.Value;
        return await db.RefreshTokens.AsNoTracking().CountAsync(token => token.UserId == id, cancellationToken);
    }

    /// <summary>
    /// The erasure requests recorded for this user - the durable record that the
    /// request was accepted, which is what a test asserting the promise was kept
    /// has to look at.
    /// </summary>
    public static Task<IReadOnlyList<OutboxMessage>> ErasureRequestsForAsync(
        this ApiFixture fixture, UserId userId, CancellationToken cancellationToken) =>
        fixture.IdentityOutboxForAsync(userId, UserDataDeletionRequested.EventType, cancellationToken);

    /// <summary>
    /// The registration announcements recorded for this user - what Billing and
    /// Applications stand their per-user state up from, and which the account
    /// cannot function without.
    /// </summary>
    public static Task<IReadOnlyList<OutboxMessage>> RegistrationAnnouncementsForAsync(
        this ApiFixture fixture, UserId userId, CancellationToken cancellationToken) =>
        fixture.IdentityOutboxForAsync(userId, UserRegistered.EventType, cancellationToken);

    /// <summary>
    /// Identity's outbox rows of one kind for one user. The rows are the module's
    /// own bookkeeping rather than an API surface, so a test that cares what was
    /// recorded has to read them directly.
    /// </summary>
    private static async Task<IReadOnlyList<OutboxMessage>> IdentityOutboxForAsync(
        this ApiFixture fixture, UserId userId, string eventType, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        return await db.Outbox.AsNoTracking()
            .Where(message => message.OwnerId == userId && message.EventType == eventType)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);
    }
}
