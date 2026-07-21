using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Contracts;

/// <summary>
/// Reads user profile facts that other modules legitimately need but must never
/// reach into Identity's tables for - the one sanctioned, synchronous way across
/// the boundary, kept deliberately small.
/// <para>
/// Currently the timezone: Notifications computes each reminder's UTC instant
/// from the user's IANA zone. More facts join this contract only when a real
/// consumer needs them.
/// </para>
/// </summary>
public interface IUserProfileQuery
{
    /// <summary>The user's IANA timezone id, or <c>null</c> when no such user exists.</summary>
    Task<string?> GetTimezoneAsync(UserId userId, CancellationToken cancellationToken);
}
