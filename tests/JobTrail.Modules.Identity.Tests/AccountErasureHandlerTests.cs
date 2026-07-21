using JobTrail.Modules.Identity.Contracts;
using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Features.DeleteAccount;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class AccountErasureHandlerTests
{
    private readonly FakeUserStore _users = new();
    private readonly AccountErasureHandler _handler;

    public AccountErasureHandlerTests() =>
        _handler = new AccountErasureHandler(AuthHarness.CreateUserManager(_users));

    [Fact]
    public async Task It_deletes_the_users_row()
    {
        var user = new ApplicationUser { UserName = "ada@example.com", Email = "ada@example.com" };
        _users.Users.Add(user);

        await _handler.HandleAsync(new UserDataDeletionRequested(UserId.From(user.Id)), CancellationToken.None);

        _users.Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task Erasing_an_already_gone_account_is_a_no_op()
    {
        var bystander = new ApplicationUser { UserName = "grace@example.com", Email = "grace@example.com" };
        _users.Users.Add(bystander);

        // At-least-once delivery: a repeat for a vanished user must not throw
        // and must leave everyone else untouched.
        await _handler.HandleAsync(new UserDataDeletionRequested(UserId.New()), CancellationToken.None);

        _users.Users.ShouldHaveSingleItem().ShouldBe(bystander);
    }
}
