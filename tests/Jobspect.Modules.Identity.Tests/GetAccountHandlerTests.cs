using Jobspect.Modules.Identity.Domain;
using Jobspect.Modules.Identity.Features.GetAccount;
using Jobspect.Modules.Identity.Tests.Fakes;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.Modules.Identity.Tests;

public sealed class GetAccountHandlerTests
{
    private readonly FakeUserStore _users = new();
    private readonly GetAccountHandler _handler;

    public GetAccountHandlerTests() =>
        _handler = new GetAccountHandler(AuthHarness.CreateUserManager(_users));

    [Fact]
    public async Task It_returns_the_callers_own_profile()
    {
        var user = new ApplicationUser
        {
            UserName = "ada@example.com",
            Email = "ada@example.com",
            TimeZoneId = "Europe/Belgrade",
            CreatedAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
        };
        _users.Users.Add(user);

        var result = await _handler.HandleAsync(UserId.From(user.Id));

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(user.Id);
        result.Value.Email.ShouldBe("ada@example.com");
        result.Value.TimeZoneId.ShouldBe("Europe/Belgrade");
        result.Value.CreatedAt.ShouldBe(user.CreatedAt);
    }

    [Fact]
    public async Task A_vanished_account_is_a_not_found()
    {
        var result = await _handler.HandleAsync(UserId.New());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("account.not_found");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }
}
