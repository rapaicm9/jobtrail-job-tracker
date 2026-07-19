using System.Buffers.Text;
using JobTrail.Modules.Identity.Authentication;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class OpaqueTokenGeneratorTests
{
    [Fact]
    public void Generated_token_carries_256_bits_of_entropy()
    {
        var token = OpaqueTokenGenerator.Generate();

        Base64Url.DecodeFromChars(token).Length.ShouldBe(32);
    }

    [Fact]
    public void Generated_token_is_url_safe()
    {
        var token = OpaqueTokenGenerator.Generate();

        // URL-safe Base64: no '+', '/' or '=' padding to escape in headers or JSON.
        token.ShouldNotContain('+');
        token.ShouldNotContain('/');
        token.ShouldNotContain('=');
    }

    [Fact]
    public void Each_generated_token_is_unique()
    {
        var tokens = Enumerable.Range(0, 1_000).Select(_ => OpaqueTokenGenerator.Generate()).ToList();

        tokens.Distinct().Count().ShouldBe(tokens.Count);
    }
}
