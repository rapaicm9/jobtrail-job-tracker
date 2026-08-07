using Jobspect.Modules.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

namespace Jobspect.Api.OpenApi;

/// <summary>
/// Describes how this API fails. Every error it returns is RFC 9457, in one of
/// two shapes; without this the document describes only the successes, and a
/// client generated from it types its error branch as nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, not annotated.</b> The alternative was a
/// <c>ProducesProblem(…)</c> line per status per endpoint - some two hundred of
/// them, hand-maintained, on forty-nine endpoints, where the failure mode is a
/// line that quietly stops being true. Everything below is read from metadata
/// the endpoints already carry, so an endpoint mapped later is described because
/// of how it was mapped.
/// </para>
/// <para>
/// <b>What is described is what an endpoint's shape implies, not what its
/// subject matter does.</b> Authorization, rate limiting, idempotency, input
/// validation and owner-scoping are properties of the shape: every endpoint of a
/// given shape has them, which is exactly why they can be derived. That a login's
/// credentials can be wrong, a campaign name taken, or a chart's dependency down
/// are properties of the subject, and no metadata carries them. Those failures
/// have the same two shapes and are told apart by the <c>code</c> member, which
/// is why a client branches on the code rather than on the status.
/// </para>
/// </remarks>
internal sealed class ProblemResponseTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    /// <summary>A single-error problem: the RFC's members, plus this API's stable error code.</summary>
    private const string ProblemSchema = "ProblemDetails";

    /// <summary>The same, and the field-keyed errors a 422 may carry instead of a code.</summary>
    private const string ValidationSchema = "ValidationProblemDetails";

    private const string MediaType = "application/problem+json";

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.AddComponent(ProblemSchema, Problem());
        document.AddComponent(ValidationSchema, Validation());

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var authorized = metadata.OfType<IAllowAnonymous>().Any() is false
            && metadata.OfType<IAuthorizeData>().Any();

        // The middleware that makes a retried mutation happen once runs on exactly
        // this shape - an authenticated POST - and refuses three ways of its own.
        var replayable = authorized && context.Description.HttpMethod is "POST";

        operation.Responses ??= [];

        if (authorized)
        {
            Describe(
                operation, context, "401", ProblemSchema,
                "No usable access token. The challenge itself sends no body; a token that is valid but "
                + "carries no subject is refused with one.");
        }

        if (metadata.OfType<IAuthorizeData>().Any(data => data.Policy?.StartsWith(
                FeaturePolicy.Prefix, StringComparison.Ordinal) is true))
        {
            Describe(
                operation, context, "403", ProblemSchema,
                "The account's plan does not include this capability. The code names which one.");
        }

        // An id in the path is an id the caller supplied. Ownership is checked
        // inside the query, so one belonging to somebody else is reported absent
        // rather than forbidden - the 404 and the genuine miss are one answer.
        if (context.Description.RelativePath?.Contains('{', StringComparison.Ordinal) is true)
        {
            Describe(operation, context, "404", ProblemSchema, "No such resource for this account.");
        }

        if (replayable)
        {
            Describe(
                operation, context, "409", ProblemSchema,
                "An earlier request carrying the same Idempotency-Key is still running. Wait Retry-After "
                + "and re-issue the same key.");
        }

        if (replayable || operation.RequestBody is not null || IsPaged(operation))
        {
            Describe(
                operation, context, "422", ValidationSchema,
                "The request was understood and refused. Field-keyed messages under errors, or a single "
                + "problem carrying a code - never both, so read whichever member is present.");
        }

        if (metadata.OfType<EnableRateLimitingAttribute>().Any())
        {
            Describe(
                operation, context, "429", ProblemSchema,
                "The caller's request budget is spent. Retry-After says for how long.");
        }

        Describe(operation, context, "500", ProblemSchema, "Something went wrong. No detail is disclosed.");

        if (replayable)
        {
            Describe(
                operation, context, "503", ProblemSchema,
                "The replay cache could not be reached, so the request was refused rather than run without "
                + "its once-only guarantee. Retry with the same key.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether this operation is one of the paged lists, which alone among the
    /// reads can refuse what it was sent.
    /// </summary>
    /// <remarks>
    /// The marker is the cursor, and it is a sound one: this API issues cursors
    /// and refuses any it did not issue or that was issued under a different
    /// ordering, through one codec shared by every list that takes one. So a
    /// cursor parameter does not merely suggest a paged list, it is the thing that
    /// gets refused. A path <c>{id}</c> is no marker at all - a malformed one
    /// fails its route constraint and never reaches a validator - and a plain
    /// query parameter is none either, since a search that finds nothing answers
    /// with an empty list rather than a complaint.
    /// </remarks>
    private static bool IsPaged(OpenApiOperation operation) =>
        operation.Parameters?.Any(parameter =>
            parameter.In is ParameterLocation.Query
            && string.Equals(parameter.Name, "cursor", StringComparison.Ordinal)) is true;

    private static void Describe(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        string status,
        string schema,
        string description)
    {
        // Never over an existing entry: an endpoint that describes one of these
        // itself has said something more specific than a rule about its shape can.
        if (operation.Responses!.ContainsKey(status))
        {
            return;
        }

        operation.Responses[status] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                [MediaType] = new() { Schema = new OpenApiSchemaReference(schema, context.Document) },
            },
        };
    }

    /// <summary>
    /// RFC 9457's members, of which every one is optional, plus the two this host
    /// adds to all of them. Written out rather than generated from the framework's
    /// <c>ProblemDetails</c>, which would describe the extensions as an anonymous
    /// bag - and the extensions are the half a client actually reads.
    /// </summary>
    private static OpenApiSchema Problem() => new()
    {
        Type = JsonSchemaType.Object,
        Description = "An RFC 9457 problem. Branch on code, never on the prose in title or detail.",
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["type"] = Text("A URI identifying the problem type."),
            ["title"] = Text("A short, stable summary of the problem type."),
            ["status"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "The HTTP status code, repeated in the body.",
            },
            ["detail"] = Text("An explanation specific to this occurrence, safe to show a user."),
            ["instance"] = Text("A URI identifying this occurrence. Usually absent."),
            ["code"] = Text(
                "This API's stable, lowercase error code - \"application.illegal_transition\". The member "
                + "to branch on. Absent on the framework's own refusals, and on a validation problem, "
                + "which keys its messages to fields instead."),
            ["traceId"] = Text(
                "The W3C trace of the request that failed. Log it; never show it. Its absence means "
                + "nothing."),
        },

        // A problem may carry further extension members, and one added later must
        // not make an existing client's parse fail.
        AdditionalProperties = new OpenApiSchema(),
    };

    /// <summary>
    /// A 422, which arrives two ways: field-keyed messages from a request
    /// validator, or a single problem with a <c>code</c> from a rule the handler
    /// enforces. One schema rather than a choice between two, because the second
    /// is a problem like any other and the first is that plus one member - so a
    /// client reads whichever is there, which is the discrimination it has to make
    /// anyway.
    /// </summary>
    private static OpenApiSchema Validation()
    {
        var schema = Problem();

        schema.Description = "A problem that may carry field-keyed validation messages.";
        schema.Properties!["errors"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description =
                "Messages by request field. A field can carry several - a password can be short and have "
                + "no digit at once - so this is a list per key, not a string.",
            AdditionalProperties = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema { Type = JsonSchemaType.String },
            },
        };

        return schema;
    }

    private static OpenApiSchema Text(string description) =>
        new() { Type = JsonSchemaType.String, Description = description };
}
