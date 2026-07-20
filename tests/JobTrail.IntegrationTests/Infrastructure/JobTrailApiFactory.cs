using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// The real host, in process, with only configuration swapped in: container
/// connection strings, test signing keys and per-test rate-limit budgets ride
/// in via <c>UseSetting</c> - no service is replaced, so the pipeline under
/// test is the one that ships.
/// </summary>
internal sealed class JobTrailApiFactory(IReadOnlyDictionary<string, string?> settings)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
