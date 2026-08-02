using System.Text.Json;
using Jobspect.SharedKernel;
using StackExchange.Redis;

namespace Jobspect.Api.Idempotency;

/// <summary>
/// Where idempotency keys live. Redis rather than a table: the records are
/// cache-shaped - they expire, they belong to no module's schema, and losing one
/// costs a duplicate rather than a fact.
/// <para>
/// Keys are scoped by owner, so one user can neither collide with nor probe
/// another's. The request itself is not part of the key but of the record's
/// fingerprint, which is what makes reusing a key for a different request a
/// detectable mistake instead of a silent wrong answer.
/// </para>
/// </summary>
internal sealed class IdempotencyStore(IConnectionMultiplexer redis, IdempotencyOptions options)
{
    private const string KeyPrefix = "jobspect:idem";

    private static readonly JsonSerializerOptions Serialization = new(JsonSerializerDefaults.General);

    /// <summary>
    /// Claims the key if it is free. Atomic - two simultaneous requests cannot
    /// both win, which is the whole of the concurrency story.
    /// </summary>
    public Task<bool> TryReserveAsync(UserId owner, string key, IdempotencyRecord reservation) =>
        Database.StringSetAsync(
            RedisKey(owner, key), Serialize(reservation), options.InFlightWindow, When.NotExists);

    public async Task<IdempotencyRecord?> ReadAsync(UserId owner, string key)
    {
        var stored = await Database.StringGetAsync(RedisKey(owner, key));

        // ToString, not the implicit conversion: a RedisValue converts to both a
        // string and a span of bytes, which leaves the overload ambiguous.
        return stored.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<IdempotencyRecord>(stored.ToString(), Serialization);
    }

    /// <summary>Stores the response for replay, for as long as the retention window.</summary>
    public Task CompleteAsync(UserId owner, string key, IdempotencyRecord reservation, IdempotencyRecord completed) =>
        WhileOursAsync(owner, key, reservation, (transaction, redisKey) =>
            transaction.StringSetAsync(redisKey, Serialize(completed), options.Retention));

    /// <summary>
    /// Frees the key. Used when the request failed with a client error: nothing
    /// changed, so the caller must be able to correct the body and send it again
    /// under the same key.
    /// </summary>
    public Task ReleaseAsync(UserId owner, string key, IdempotencyRecord reservation) =>
        WhileOursAsync(owner, key, reservation, (transaction, redisKey) => transaction.KeyDeleteAsync(redisKey));

    /// <summary>
    /// Applies a change only while the stored record is still the reservation this
    /// request wrote. A slow request can outlive its own reservation, and by then
    /// the key may legitimately belong to another request - whose record must not
    /// be overwritten by this one's late arrival.
    /// </summary>
    private async Task WhileOursAsync(
        UserId owner, string key, IdempotencyRecord reservation, Func<ITransaction, string, Task> change)
    {
        var redisKey = RedisKey(owner, key);

        var transaction = Database.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(redisKey, Serialize(reservation)));

        // Deliberately not awaited: inside a transaction this task completes only
        // once Execute runs, so awaiting it here would deadlock.
        _ = change(transaction, redisKey);

        await transaction.ExecuteAsync();
    }

    private IDatabase Database => redis.GetDatabase();

    private static string Serialize(IdempotencyRecord record) => JsonSerializer.Serialize(record, Serialization);

    private static string RedisKey(UserId owner, string key) => $"{KeyPrefix}:{owner.Value:N}:{key}";
}
