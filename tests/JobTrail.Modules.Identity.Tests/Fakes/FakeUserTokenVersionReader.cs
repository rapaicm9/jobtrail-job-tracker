using JobTrail.Modules.Identity.Authentication;
using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IUserTokenVersionReader"/>. A user absent from
/// <see cref="Versions"/> reads back as <c>null</c>, standing in for an account
/// that no longer exists.
/// </summary>
internal sealed class FakeUserTokenVersionReader : IUserTokenVersionReader
{
    public Dictionary<Guid, int> Versions { get; } = [];

    public Task<int?> GetTokenVersionAsync(UserId userId, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.TryGetValue(userId.Value, out var version) ? version : (int?)null);
}
