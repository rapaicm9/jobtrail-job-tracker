using JobTrail.Modules.Identity.Authentication;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class RefreshTokenHasherTests
{
    [Fact]
    public void Hash_is_a_256_bit_digest()
    {
        var hash = RefreshTokenHasher.Hash("some-opaque-token");

        hash.Length.ShouldBe(32);
    }

    [Fact]
    public void Hashing_the_same_token_is_deterministic()
    {
        const string token = "the-same-opaque-token";

        RefreshTokenHasher.Hash(token).ShouldBe(RefreshTokenHasher.Hash(token));
    }

    [Fact]
    public void Different_tokens_hash_differently()
    {
        RefreshTokenHasher.Hash("token-a").ShouldNotBe(RefreshTokenHasher.Hash("token-b"));
    }
}
