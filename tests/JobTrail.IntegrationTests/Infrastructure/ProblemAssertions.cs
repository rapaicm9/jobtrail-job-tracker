using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace JobTrail.IntegrationTests.Infrastructure;

internal static class ProblemAssertions
{
    /// <summary>
    /// Asserts the RFC 9457 shape end to end: status, problem+json media type,
    /// echoed status field, and the machine-readable <c>code</c> extension.
    /// </summary>
    public static async Task<ProblemDetails> ShouldBeProblemAsync(
        this HttpResponseMessage response, int status, string? code = null)
    {
        ((int)response.StatusCode).ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>()).ShouldNotBeNull();
        problem.Status.ShouldBe(status);

        if (code is not null)
        {
            problem.Extensions.ShouldContainKey("code");
            ((JsonElement)problem.Extensions["code"]!).GetString().ShouldBe(code);
        }

        return problem;
    }

    /// <summary>Asserts a 422 whose error dictionary covers exactly the given fields.</summary>
    public static async Task<HttpValidationProblemDetails> ShouldBeValidationProblemAsync(
        this HttpResponseMessage response, params string[] fields)
    {
        ((int)response.StatusCode).ShouldBe(StatusCodes.Status422UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())
            .ShouldNotBeNull();
        problem.Errors.Keys.ShouldBe(fields, ignoreOrder: true);
        return problem;
    }
}
