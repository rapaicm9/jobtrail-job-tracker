using JobTrail.Modules.Identity.Features.Register;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class RegisterRequestValidatorTests
{
    private static RegisterRequest Valid(
        string? email = "ada@example.com",
        string? password = "Correct-horse7",
        string? timeZoneId = null,
        string? deviceLabel = null) => new(email, password, timeZoneId, deviceLabel);

    [Fact]
    public void A_valid_request_produces_no_errors() =>
        RegisterRequestValidator.Validate(Valid()).ShouldBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_email_is_rejected(string? email) =>
        RegisterRequestValidator.Validate(Valid(email: email)).ShouldNotBeNull()
            .ShouldContainKey("email");

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("no-at-sign.example.com")]
    public void A_malformed_email_is_rejected(string email) =>
        RegisterRequestValidator.Validate(Valid(email: email)).ShouldNotBeNull()
            .ShouldContainKey("email");

    [Fact]
    public void A_missing_password_is_rejected() =>
        RegisterRequestValidator.Validate(Valid(password: null)).ShouldNotBeNull()
            .ShouldContainKey("password");

    [Theory]
    [InlineData("Sh0rt-!", "at least 8 characters")]
    [InlineData("correct-horse7", "uppercase letter")]
    [InlineData("CORRECT-HORSE7", "lowercase letter")]
    [InlineData("Correct-horse", "digit")]
    [InlineData("Correcthorse7", "special character")]
    public void Each_password_rule_is_enforced(string password, string expectedFragment)
    {
        var errors = RegisterRequestValidator.Validate(Valid(password: password)).ShouldNotBeNull();

        errors["password"].ShouldHaveSingleItem().ShouldContain(expectedFragment);
    }

    [Fact]
    public void A_password_failing_several_rules_reports_all_of_them()
    {
        var errors = RegisterRequestValidator.Validate(Valid(password: "short")).ShouldNotBeNull();

        // length, uppercase, digit, special - everything except the lowercase rule.
        errors["password"].Length.ShouldBe(4);
    }

    [Theory]
    [InlineData("Europe/Belgrade")]
    [InlineData("America/Argentina/Ushuaia")]
    [InlineData("Etc/UTC")]
    [InlineData("UTC")]
    public void Iana_timezones_are_accepted(string timeZoneId) =>
        RegisterRequestValidator.Validate(Valid(timeZoneId: timeZoneId)).ShouldBeNull();

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("Central Europe Standard Time")] // Windows id, resolvable but not IANA
    public void Non_iana_timezones_are_rejected(string timeZoneId) =>
        RegisterRequestValidator.Validate(Valid(timeZoneId: timeZoneId)).ShouldNotBeNull()
            .ShouldContainKey("timeZoneId");

    [Fact]
    public void An_overlong_device_label_is_rejected() =>
        RegisterRequestValidator.Validate(Valid(deviceLabel: new string('x', 129))).ShouldNotBeNull()
            .ShouldContainKey("deviceLabel");

    [Fact]
    public void Every_invalid_field_is_reported_at_once()
    {
        var errors = RegisterRequestValidator
            .Validate(new RegisterRequest(null, null, "Not/AZone", new string('x', 129)))
            .ShouldNotBeNull();

        errors.Keys.ShouldBe(["email", "password", "timeZoneId", "deviceLabel"], ignoreOrder: true);
    }
}
