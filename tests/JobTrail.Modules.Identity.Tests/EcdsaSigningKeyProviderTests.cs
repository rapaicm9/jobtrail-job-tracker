using JobTrail.Modules.Identity.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class EcdsaSigningKeyProviderTests
{
    [Fact]
    public async Task Private_key_signs_what_the_public_key_validates()
    {
        var provider = new EcdsaSigningKeyProvider(TestKeys.NewOptions().Wrap());
        var handler = new JsonWebTokenHandler();

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "jobtrail",
            SigningCredentials = provider.SigningCredentials,
        });

        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "jobtrail",
            IssuerSigningKey = provider.PublicKey,
            ValidateAudience = false,
            ValidateLifetime = false,
        });

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Signing_credentials_use_es256()
    {
        var provider = new EcdsaSigningKeyProvider(TestKeys.NewOptions().Wrap());

        provider.SigningCredentials.Algorithm.ShouldBe(SecurityAlgorithms.EcdsaSha256);
    }

    [Fact]
    public void A_missing_private_key_fails_loudly_on_first_use_only()
    {
        // Construction must not throw - a host with no keys still starts.
        var provider = new EcdsaSigningKeyProvider(TestKeys.NewOptions(o => o.PrivateKeyPem = string.Empty).Wrap());

        Should.Throw<InvalidOperationException>(() => provider.SigningCredentials)
            .Message.ShouldContain("PrivateKeyPem");
    }
}
