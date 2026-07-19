using JobTrail.Modules.Identity.Features.Register;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class RegisterHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserStore _users = new();
    private readonly FakeRefreshTokenStore _refreshTokens = new();
    private readonly RegisterHandler _handler;

    public RegisterHandlerTests()
    {
        var userManager = AuthHarness.CreateUserManager(_users);
        var tokenService = AuthHarness.CreateTokenService(
            _refreshTokens, new FakeUserTokenVersionReader(), TestKeys.NewOptions(), new TestTimeProvider(Now));
        _handler = new RegisterHandler(userManager, tokenService);
    }

    [Fact]
    public async Task Registering_creates_the_account_and_signs_the_user_in()
    {
        var request = new RegisterRequest("ada@example.com", "Correct-horse7", "Europe/Belgrade", "Pixel 8");

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var user = _users.Users.ShouldHaveSingleItem();
        result.Value.UserId.ShouldBe(user.Id);
        user.Email.ShouldBe("ada@example.com");
        user.TimeZoneId.ShouldBe("Europe/Belgrade");

        // The password is hashed, never stored raw.
        user.PasswordHash.ShouldNotBeNullOrEmpty();
        user.PasswordHash.ShouldNotContain("Correct-horse7");

        // Signed straight in: one refresh-token row for the new account.
        _refreshTokens.Tokens.ShouldHaveSingleItem().UserId.ShouldBe(user.Id);
        result.Value.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Omitting_the_timezone_defaults_to_utc()
    {
        var result = await _handler.HandleAsync(
            new RegisterRequest("ada@example.com", "Correct-horse7", TimeZoneId: null, DeviceLabel: null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _users.Users.Single().TimeZoneId.ShouldBe("Etc/UTC");
    }

    [Fact]
    public async Task Registering_a_taken_email_is_a_conflict()
    {
        await _handler.HandleAsync(
            new RegisterRequest("ada@example.com", "Correct-horse7", null, null), CancellationToken.None);

        var second = await _handler.HandleAsync(
            new RegisterRequest("ada@example.com", "Other-passw0rd!", null, null), CancellationToken.None);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("registration.email_taken");
        second.Error.Type.ShouldBe(ErrorType.Conflict);
        _users.Users.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Identity_backstops_the_password_policy_when_the_request_validator_is_bypassed()
    {
        // No uppercase letter - the endpoint's validator would have caught this,
        // but the handler must not rely on it (defence in depth).
        var result = await _handler.HandleAsync(
            new RegisterRequest("ada@example.com", "correct-horse7", null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("registration.invalid");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _users.Users.ShouldBeEmpty();
    }
}
