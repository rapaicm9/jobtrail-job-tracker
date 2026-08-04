using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The committed contract, and the gate that keeps it honest.
/// </summary>
/// <remarks>
/// <para>
/// The clients generate from <c>docs/openapi/openapi.yaml</c>, so it has to be
/// tracked - and a tracked copy of a generated thing goes stale silently. This
/// test regenerates it from the host that serves it and fails when the two have
/// parted, which turns "someone forgot" into a red build.
/// </para>
/// <para>
/// The file is produced here rather than at build time for one reason: .NET 10
/// emits YAML only from the served endpoint, and the build-time generator would
/// have to boot this host with no connection strings to reach it. This suite
/// already has the host running against real containers, so the artefact comes
/// from the same endpoint the clients read.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class OpenApiContractTests(ApiFixture fixture)
{
    /// <summary>
    /// Set to rewrite the committed document instead of asserting against it -
    /// the deliberate act that accompanies a contract change:
    /// <c>JOBSPECT_WRITE_OPENAPI=1 dotnet test</c>.
    /// </summary>
    private const string WriteVariable = "JOBSPECT_WRITE_OPENAPI";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_committed_document_matches_the_one_this_host_serves()
    {
        var served = Normalize(await ServedDocumentAsync());
        var path = CommittedDocumentPath();

        if (Environment.GetEnvironmentVariable(WriteVariable) is { Length: > 0 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, served, Ct);
        }

        File.Exists(path).ShouldBeTrue($"{path} is missing; regenerate it with {WriteVariable}=1.");

        var committed = Normalize(await File.ReadAllTextAsync(path, Ct));

        committed.ShouldBe(
            served,
            $"The committed contract has drifted from the API. Regenerate it with {WriteVariable}=1 and commit "
            + "the result - and read the diff, because anything in it is a change the clients will see.");
    }

    /// <summary>
    /// Written with LF and one trailing newline regardless of platform, so the
    /// comparison is about the contract and never about line endings.
    /// </summary>
    private static string Normalize(string document) =>
        document.ReplaceLineEndings("\n").TrimEnd() + "\n";

    /// <summary>
    /// Walks out of the test's bin directory to the solution file. The path has
    /// to be found rather than configured: the same test writes the artefact on
    /// a developer's machine and reads it in CI.
    /// </summary>
    private static string CommittedDocumentPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jobspect.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the repository root should be an ancestor of the test output directory");

        return Path.Combine(directory.FullName, "docs", "openapi", "openapi.yaml");
    }

    private async Task<string> ServedDocumentAsync()
    {
        var response = await fixture.CreateClient().GetAsync("/openapi/v1.yaml", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync(Ct);
    }
}
