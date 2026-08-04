using System.Net;
using System.Text.Json;
using Jobspect.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The described contract, asserted against the host that serves it. These pin
/// the four things the description would otherwise get wrong silently - a client
/// generated from a wrong document fails at the client, far from here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpenApiDocumentTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Paths_carry_the_resolved_version_not_the_route_parameter()
    {
        using var document = await GetDocumentAsync();

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToList();

        paths.ShouldNotBeEmpty();
        paths.ShouldAllBe(path => path.StartsWith("/api/v1/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_bearer_scheme_is_declared_once_and_required_only_where_it_applies()
    {
        using var document = await GetDocumentAsync();

        document.RootElement
            .GetProperty("components").GetProperty("securitySchemes").GetProperty("bearer")
            .GetProperty("scheme").GetString()
            .ShouldBe("bearer");

        var operations = Operations(document).ToList();

        // Registering, logging in and refreshing are how a caller gets a token;
        // requiring one would describe a contract nobody could enter.
        operations.Where(operation => operation.Secured)
            .ShouldNotContain(operation => operation.Path.EndsWith("/identity/register", StringComparison.Ordinal));

        operations.Single(operation =>
                operation.Path == "/api/v1/account" && operation.Method == "get")
            .Secured.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_operation_is_tagged_with_exactly_one_resource()
    {
        using var document = await GetDocumentAsync();

        foreach (var operation in Operations(document))
        {
            operation.Tags.ShouldHaveSingleItem();
        }
    }

    /// <summary>
    /// The generator fills this in from the request that fetched the document,
    /// which in this suite is a random loopback port. An empty list is what makes
    /// the document the same artefact wherever it was produced.
    /// </summary>
    [Fact]
    public async Task The_document_names_no_server()
    {
        using var document = await GetDocumentAsync();

        document.RootElement.TryGetProperty("servers", out var servers).ShouldBeFalse();
    }

    [Fact]
    public async Task The_document_is_served_as_yaml_too()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/openapi/v1.yaml", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldStartWith("openapi:");
    }

    private static IEnumerable<(string Path, string Method, bool Secured, List<string> Tags)> Operations(
        JsonDocument document)
    {
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var tags = operation.Value.TryGetProperty("tags", out var declared)
                    ? declared.EnumerateArray().Select(tag => tag.GetString()!).ToList()
                    : [];

                yield return (path.Name, operation.Name, operation.Value.TryGetProperty("security", out _), tags);
            }
        }
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }
}
