using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// Reads a user's current token version - the value a freshly minted access
/// token must carry - during a refresh, without the token service reaching into
/// the user store directly. Returns <c>null</c> if the user no longer exists.
/// </summary>
internal interface IUserTokenVersionReader
{
    Task<int?> GetTokenVersionAsync(UserId userId, CancellationToken cancellationToken);
}
