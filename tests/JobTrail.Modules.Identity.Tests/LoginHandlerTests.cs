using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Features.Login;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class LoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserStore _users = new();
    private readonly FakeRefreshTokenStore _refreshTokens = new();
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly LoginHandler _handler;

    public LoginHandlerTests()
    {
        _userManager = AuthHarness.CreateUserManager(_users);
        var tokenService = AuthHarness.CreateTokenService(
            _refreshTokens, new FakeUserTokenVersionReader(), TestKeys.NewOptions(), new TestTimeProvider(Now));
        _handler = new LoginHandler(_userManager, tokenService);
    }

    [Fact]
    public async Task Correct_credentials_sign_the_user_in()
    {
        var user = await SeedUserAsync("ada@example.com", "Correct-horse7");

        var result = await _handler.HandleAsync(
            new LoginRequest("ada@example.com", "Correct-horse7", "Firefox on Linux"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(user.Id);
        result.Value.AccessToken.ShouldNotBeNullOrEmpty();

        var refreshRow = _refreshTokens.Tokens.ShouldHaveSingleItem();
        refreshRow.UserId.ShouldBe(user.Id);
        refreshRow.DeviceLabel.ShouldBe("Firefox on Linux");
    }

    [Fact]
    public async Task A_wrong_password_is_rejected()
    {
        await SeedUserAsync("ada@example.com", "Correct-horse7");

        var result = await _handler.HandleAsync(
            new LoginRequest("ada@example.com", "Wrong-horse7", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("auth.invalid_credentials");
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
        _refreshTokens.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_email_fails_with_the_same_error_as_a_wrong_password()
    {
        await SeedUserAsync("ada@example.com", "Correct-horse7");

        var unknownEmail = await _handler.HandleAsync(
            new LoginRequest("nobody@example.com", "Correct-horse7", null), CancellationToken.None);
        var wrongPassword = await _handler.HandleAsync(
            new LoginRequest("ada@example.com", "Wrong-horse7", null), CancellationToken.None);

        // Indistinguishable outcomes: the endpoint never confirms an address has an account.
        unknownEmail.IsFailure.ShouldBeTrue();
        unknownEmail.Error.ShouldBe(wrongPassword.Error);
    }

    private async Task<ApplicationUser> SeedUserAsync(string email, string password)
    {
        var user = new ApplicationUser { UserName = email, Email = email };
        (await _userManager.CreateAsync(user, password)).Succeeded.ShouldBeTrue();
        return user;
    }
}
