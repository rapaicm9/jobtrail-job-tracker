using JobTrail.Modules.Identity.Features.UpdateAccount;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class UpdateAccountRequestValidatorTests
{
    [Theory]
    [InlineData("Europe/Belgrade")]
    [InlineData("America/Argentina/Ushuaia")]
    [InlineData("Etc/UTC")]
    [InlineData("UTC")]
    public void Iana_timezones_are_accepted(string timeZoneId) =>
        UpdateAccountRequestValidator.Validate(new UpdateAccountRequest(timeZoneId)).ShouldBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_timezone_is_rejected(string? timeZoneId) =>
        UpdateAccountRequestValidator.Validate(new UpdateAccountRequest(timeZoneId)).ShouldNotBeNull()
            .ShouldContainKey("timeZoneId");

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("Central Europe Standard Time")] // Windows id, resolvable but not IANA
    public void Non_iana_timezones_are_rejected(string timeZoneId) =>
        UpdateAccountRequestValidator.Validate(new UpdateAccountRequest(timeZoneId)).ShouldNotBeNull()
            .ShouldContainKey("timeZoneId");

    [Fact]
    public void An_overlong_timezone_is_rejected() =>
        UpdateAccountRequestValidator.Validate(new UpdateAccountRequest(new string('x', 65))).ShouldNotBeNull()
            .ShouldContainKey("timeZoneId");
}
