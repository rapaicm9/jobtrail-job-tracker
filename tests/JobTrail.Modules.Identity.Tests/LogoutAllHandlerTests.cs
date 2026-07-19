using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Features.LogoutAll;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class LogoutAllHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserStore _users = new();
    private readonly FakeRefreshTokenStore _refreshTokens = new();
    private readonly RefreshTokenService _refreshTokenService;
    private readonly LogoutAllHandler _handler;

    public LogoutAllHandlerTests()
    {
        var userManager = AuthHarness.CreateUserManager(_users);
        _refreshTokenService = new RefreshTokenService(
            _refreshTokens, TestKeys.NewOptions().Wrap(), new TestTimeProvider(Now));
        _handler = new LogoutAllHandler(userManager, _refreshTokenService);
    }

    [Fact]
    public async Task Global_logout_bumps_the_token_version_and_deletes_every_session()
    {
        var user = new ApplicationUser { UserName = "ada@example.com", Email = "ada@example.com" };
        _users.Users.Add(user);
        var bystander = UserId.New();

        await _refreshTokenService.IssueAsync(UserId.From(user.Id), "Pixel 8", CancellationToken.None);
        await _refreshTokenService.IssueAsync(UserId.From(user.Id), "Firefox on Linux", CancellationToken.None);
        await _refreshTokenService.IssueAsync(bystander, null, CancellationToken.None);

        var result = await _handler.HandleAsync(UserId.From(user.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.TokenVersion.ShouldBe(1);

        // Only the bystander's session survives.
        _refreshTokens.Tokens.ShouldHaveSingleItem().UserId.ShouldBe(bystander.Value);
    }

    [Fact]
    public async Task A_vanished_account_cannot_log_out_globally()
    {
        var result = await _handler.HandleAsync(UserId.New(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("auth.user_not_found");
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
    }
}
