using JobTrail.Modules.Identity.Features.Logout;
using JobTrail.Modules.Identity.Features.Refresh;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

/// <summary>The two single-field slices share one shape: a required refresh token.</summary>
public sealed class TokenRequestValidatorTests
{
    [Fact]
    public void A_present_refresh_token_passes_both_validators()
    {
        RefreshRequestValidator.Validate(new RefreshRequest("some-token")).ShouldBeNull();
        LogoutRequestValidator.Validate(new LogoutRequest("some-token")).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_refresh_token_fails_both_validators(string? token)
    {
        RefreshRequestValidator.Validate(new RefreshRequest(token)).ShouldNotBeNull()
            .ShouldContainKey("refreshToken");
        LogoutRequestValidator.Validate(new LogoutRequest(token)).ShouldNotBeNull()
            .ShouldContainKey("refreshToken");
    }
}
