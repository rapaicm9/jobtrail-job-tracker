using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Features.UpdateAccount;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class UpdateAccountHandlerTests
{
    private readonly FakeUserStore _users = new();
    private readonly UpdateAccountHandler _handler;

    public UpdateAccountHandlerTests() =>
        _handler = new UpdateAccountHandler(AuthHarness.CreateUserManager(_users));

    [Fact]
    public async Task It_updates_the_timezone_and_returns_the_fresh_profile()
    {
        var user = new ApplicationUser
        {
            UserName = "ada@example.com",
            Email = "ada@example.com",
            TimeZoneId = "Etc/UTC",
        };
        _users.Users.Add(user);

        var result = await _handler.HandleAsync(
            UserId.From(user.Id), new UpdateAccountRequest("Europe/Belgrade"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.TimeZoneId.ShouldBe("Europe/Belgrade");
        user.TimeZoneId.ShouldBe("Europe/Belgrade");
    }

    [Fact]
    public async Task Updating_a_vanished_account_is_a_not_found()
    {
        var result = await _handler.HandleAsync(
            UserId.New(), new UpdateAccountRequest("Europe/Belgrade"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("account.not_found");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }
}
