using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel;

namespace Jobspect.IntegrationTests.Infrastructure;

/// <summary>
/// Answers every account with one timezone.
/// <para>
/// For the tests that drive a handler directly, where registering a real account
/// would prove nothing the wiring tests do not already prove and would tie each
/// case to an HTTP round trip. The real <see cref="IUserProfileQuery"/> is
/// exercised where it belongs - in the tests that go through the host.
/// </para>
/// </summary>
internal sealed class StubProfileQuery(string? timeZoneId) : IUserProfileQuery
{
    public Task<string?> GetTimezoneAsync(UserId userId, CancellationToken cancellationToken) =>
        Task.FromResult(timeZoneId);
}
