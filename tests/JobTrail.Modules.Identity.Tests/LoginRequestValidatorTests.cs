using JobTrail.Modules.Identity.Features.Login;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class LoginRequestValidatorTests
{
    [Fact]
    public void A_valid_request_produces_no_errors() =>
        LoginRequestValidator.Validate(new LoginRequest("ada@example.com", "whatever", "Pixel 8"))
            .ShouldBeNull();

    [Fact]
    public void Missing_credentials_are_reported_field_by_field()
    {
        var errors = LoginRequestValidator.Validate(new LoginRequest(null, null, null)).ShouldNotBeNull();

        errors.Keys.ShouldBe(["email", "password"], ignoreOrder: true);
    }

    [Fact]
    public void The_password_policy_is_not_echoed_on_login()
    {
        // "short" violates every composition rule, but login must not reveal the
        // policy - presence is the only check.
        LoginRequestValidator.Validate(new LoginRequest("ada@example.com", "short", null))
            .ShouldBeNull();
    }

    [Fact]
    public void An_overlong_device_label_is_rejected() =>
        LoginRequestValidator.Validate(new LoginRequest("ada@example.com", "whatever", new string('x', 129)))
            .ShouldNotBeNull()
            .ShouldContainKey("deviceLabel");
}
