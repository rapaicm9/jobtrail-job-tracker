using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.Modules.Identity.Features.Register;
using JobTrail.Modules.Identity.Persistence;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class RegisterHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserStore _users = new();
    private readonly FakeRefreshTokenStore _refreshTokens = new();

    /// <summary>
    /// A real context that is never connected to anything. The handler's only use
    /// of it is to add the announcement to the change tracker, and the account
    /// itself is written through the fake store - so nothing here ever reaches a
    /// database, and the row stays inspectable where the handler left it. What the
    /// context cannot show is the part that needs a real one: that the row and the
    /// user reach the database in a single save. That is asserted over Postgres.
    /// </summary>
    private readonly IdentityModuleDbContext _dbContext = new IdentityModuleDbContextFactory().CreateDbContext([]);

    private readonly RegisterHandler _handler;

    public RegisterHandlerTests()
    {
        var userManager = AuthHarness.CreateUserManager(_users);
        var tokenService = AuthHarness.CreateTokenService(
            _refreshTokens, new FakeUserTokenVersionReader(), TestKeys.NewOptions(), new TestTimeProvider(Now));
        _handler = new RegisterHandler(userManager, _dbContext, tokenService);
    }

    public void Dispose() => _dbContext.Dispose();

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

        // The new account is announced for the modules that own per-user state.
        var announced = Announcements().ShouldHaveSingleItem();
        announced.EventType.ShouldBe(UserRegistered.EventType);
        announced.OwnerId.ShouldBe(UserId.From(user.Id));

        // The address is the one thing a consumer must not learn from the stream.
        announced.Payload.ShouldContain(user.Id.ToString());
        announced.Payload.ShouldNotContain("ada@example.com");
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

        // Exactly one account opened, so exactly one announcement - the rejected
        // attempt takes its own back rather than leaving it for a later save.
        Announcements().ShouldHaveSingleItem();
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

        // No account, no announcement.
        Announcements().ShouldBeEmpty();
    }

    /// <summary>What the handler has queued for delivery, still on the change tracker.</summary>
    private IReadOnlyList<OutboxMessage> Announcements() => [.. _dbContext.Outbox.Local];
}
