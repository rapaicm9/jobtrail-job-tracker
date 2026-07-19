using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class AccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private static (AccessTokenIssuer Issuer, EcdsaSigningKeyProvider Keys, JwtOptions Options) Build(
        Action<JwtOptions>? configure = null)
    {
        var options = TestKeys.NewOptions(configure);
        var keys = new EcdsaSigningKeyProvider(options.Wrap());
        var issuer = new AccessTokenIssuer(keys, options.Wrap(), new TestTimeProvider(Now));
        return (issuer, keys, options);
    }

    [Fact]
    public async Task Issued_token_validates_against_the_public_key()
    {
        var (issuer, keys, options) = Build();

        var token = issuer.Issue(UserId.New(), tokenVersion: 0);
        var result = await Validate(token.Value, keys, options);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Issued_token_is_signed_with_es256()
    {
        var (issuer, _, _) = Build();

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(issuer.Issue(UserId.New(), 0).Value);

        jwt.Alg.ShouldBe(SecurityAlgorithms.EcdsaSha256); // "ES256"
    }

    [Fact]
    public void Issued_token_carries_only_sub_jti_and_token_version()
    {
        var (issuer, _, _) = Build();
        var userId = UserId.New();

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(issuer.Issue(userId, tokenVersion: 7).Value);

        jwt.Subject.ShouldBe(userId.ToString());
        jwt.GetClaim(AccessTokenIssuer.TokenVersionClaim).Value.ShouldBe("7");
        jwt.GetClaim(JwtRegisteredClaimNames.Jti).Value.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Issued_token_contains_no_pii()
    {
        var (issuer, _, _) = Build();

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(issuer.Issue(UserId.New(), 0).Value);

        string[] piiClaims = ["email", "name", "given_name", "family_name", "phone_number"];
        jwt.Claims.Select(c => c.Type).ShouldNotContain(type => piiClaims.Contains(type));
    }

    [Fact]
    public void Access_token_expires_after_the_configured_lifetime()
    {
        var (issuer, _, _) = Build(o => o.AccessTokenLifetime = TimeSpan.FromMinutes(12));

        var token = issuer.Issue(UserId.New(), 0);

        token.ExpiresAt.ShouldBe(Now.AddMinutes(12));
    }

    [Fact]
    public async Task Token_from_a_different_key_fails_validation()
    {
        var (issuer, _, options) = Build();
        var otherKeys = new EcdsaSigningKeyProvider(TestKeys.NewOptions().Wrap());

        var result = await Validate(issuer.Issue(UserId.New(), 0).Value, otherKeys, options);

        result.IsValid.ShouldBeFalse();
    }

    private static Task<TokenValidationResult> Validate(
        string token, ISigningKeyProvider keys, JwtOptions options) =>
        new JsonWebTokenHandler().ValidateTokenAsync(
            token,
            new TokenValidationParameters
            {
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                IssuerSigningKey = keys.PublicKey,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = false, // lifetime is asserted separately, off the wall clock
                ClockSkew = TimeSpan.Zero,
            });
}
