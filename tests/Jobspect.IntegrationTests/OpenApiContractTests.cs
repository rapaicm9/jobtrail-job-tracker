using System.Net;
using System.Text.Json;
using Jobspect.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The committed contract, and the gates that keep it honest.
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

    /// <summary>The keywords under which a schema can hold alternatives to itself.</summary>
    private static readonly string[] Compositions = ["oneOf", "anyOf", "allOf"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_committed_document_matches_the_one_this_host_serves()
    {
        var served = Normalize(await ServedDocumentAsync("yaml"));
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
    /// Requests describe their enum-shaped fields as plain strings, and this is
    /// the rule rather than an accident of how they were written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The responses hold the enums themselves, which is what puts the member
    /// sets in the document. Doing the same on the way in would look like an
    /// improvement and is not: an unknown value would then be refused by the
    /// model binder, which answers a bare 400 naming no field, in place of the
    /// field-keyed 422 every other bad value in this API gets. The handlers parse
    /// leniently instead - <c>Enum.TryParse(ignoreCase: true)</c> - and their
    /// validators key the failure to the property.
    /// </para>
    /// <para>
    /// Asserted over the document rather than over the request records, because
    /// what matters is the shape a client is told to send, and because this way
    /// one test covers every request type including the ones added later. Before
    /// the responses carried real enums a slip here wrote integers and was
    /// obvious; now it works, and quietly downgrades the refusal.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task No_request_schema_constrains_a_field_to_an_enum()
    {
        // The JSON rendering of the same document: this reads it rather than
        // diffs it, and the committed copy is YAML only because a human reads it.
        using var document = JsonDocument.Parse(await ServedDocumentAsync("json"));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var name in RequestSchemaNames(document.RootElement))
        {
            CollectEnums(schemas, name, name, offenders, []);
        }

        offenders.ShouldBeEmpty(
            "a request field is described as an enum, so an unknown value is now refused by the model "
            + "binder as a bare 400 instead of by a validator as a field-keyed 422. Type the property as "
            + "string and parse it in the handler.");
    }

    /// <summary>The component schemas the operations name as their request bodies.</summary>
    private static IEnumerable<string> RequestSchemaNames(JsonElement root)
    {
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Value.TryGetProperty("requestBody", out var body)
                    && body.TryGetProperty("content", out var content)
                    && content.TryGetProperty("application/json", out var json)
                    && json.TryGetProperty("schema", out var schema)
                    && ReferenceName(schema) is { } name)
                {
                    yield return name;
                }
            }
        }
    }

    /// <summary>
    /// Walks one request schema and everything it reaches, reporting each field
    /// that carries an <c>enum</c>. A request body is not always flat - a
    /// compensation is its own schema - so following the references is what makes
    /// this a rule about requests rather than about top-level properties.
    /// </summary>
    private static void CollectEnums(
        JsonElement schemas, string name, string path, SortedSet<string> offenders, HashSet<string> seen)
    {
        if (!seen.Add(name) || !schemas.TryGetProperty(name, out var schema))
        {
            return;
        }

        if (!schema.TryGetProperty("properties", out var properties))
        {
            return;
        }

        foreach (var property in properties.EnumerateObject())
        {
            var where = $"{path}.{property.Name}";

            if (property.Value.TryGetProperty("enum", out _))
            {
                offenders.Add(where);
            }

            foreach (var referenced in ReferencesOf(property.Value))
            {
                if (schemas.TryGetProperty(referenced, out var target) && target.TryGetProperty("enum", out _))
                {
                    offenders.Add(where);
                }

                CollectEnums(schemas, referenced, where, offenders, seen);
            }
        }
    }

    /// <summary>
    /// Every component a property can reach in one hop: directly, through the
    /// items of an array, or through one branch of the null-or-this wrapper the
    /// generator emits for a nullable one.
    /// </summary>
    private static IEnumerable<string> ReferencesOf(JsonElement property)
    {
        if (ReferenceName(property) is { } direct)
        {
            yield return direct;
        }

        if (property.TryGetProperty("items", out var items) && ReferenceName(items) is { } element)
        {
            yield return element;
        }

        foreach (var keyword in Compositions)
        {
            if (!property.TryGetProperty(keyword, out var branches))
            {
                continue;
            }

            foreach (var branch in branches.EnumerateArray())
            {
                if (ReferenceName(branch) is { } alternative)
                {
                    yield return alternative;
                }
            }
        }
    }

    private static string? ReferenceName(JsonElement schema) =>
        schema.ValueKind is JsonValueKind.Object
        && schema.TryGetProperty("$ref", out var reference)
        && reference.GetString() is { } pointer
            ? pointer[(pointer.LastIndexOf('/') + 1)..]
            : null;

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

    private async Task<string> ServedDocumentAsync(string format)
    {
        var response = await fixture.CreateClient().GetAsync($"/openapi/v1.{format}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync(Ct);
    }
}
