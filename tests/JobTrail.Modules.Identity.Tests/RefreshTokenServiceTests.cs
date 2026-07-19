using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class RefreshTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeRefreshTokenStore _store = new();
    private readonly TestTimeProvider _clock = new(Now);
    private readonly RefreshTokenService _service;
    private readonly JwtOptions _options = TestKeys.NewOptions(o => o.RefreshTokenLifetime = TimeSpan.FromDays(30));

    public RefreshTokenServiceTests() =>
        _service = new RefreshTokenService(_store, _options.Wrap(), _clock);

    [Fact]
    public async Task Issuing_creates_a_single_live_token()
    {
        var userId = UserId.New();

        var issued = await _service.IssueAsync(userId, "Pixel 8", CancellationToken.None);

        issued.RawToken.ShouldNotBeNullOrEmpty();
        issued.ExpiresAt.ShouldBe(Now.AddDays(30));

        var stored = _store.Tokens.ShouldHaveSingleItem();
        stored.UserId.ShouldBe(userId.Value);
        stored.RevokedAt.ShouldBeNull();
        stored.DeviceLabel.ShouldBe("Pixel 8");
        stored.TokenHash.ShouldBe(RefreshTokenHasher.Hash(issued.RawToken));
    }

    [Fact]
    public async Task The_raw_token_is_never_stored()
    {
        var issued = await _service.IssueAsync(UserId.New(), null, CancellationToken.None);

        var storedHash = _store.Tokens.Single().TokenHash;
        storedHash.ShouldNotBe(System.Text.Encoding.UTF8.GetBytes(issued.RawToken));
        storedHash.ShouldBe(RefreshTokenHasher.Hash(issued.RawToken));
    }

    [Fact]
    public async Task Each_login_starts_a_new_family()
    {
        await _service.IssueAsync(UserId.New(), null, CancellationToken.None);
        await _service.IssueAsync(UserId.New(), null, CancellationToken.None);

        _store.Tokens.Select(t => t.FamilyId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Rotating_retires_the_old_token_and_issues_a_replacement_in_the_same_family()
    {
        var userId = UserId.New();
        var issued = await _service.IssueAsync(userId, "Pixel 8", CancellationToken.None);
        var familyId = _store.Tokens.Single().FamilyId;

        var result = await _service.RotateAsync(issued.RawToken, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(userId);
        result.Value.RawToken.ShouldNotBe(issued.RawToken);

        _store.Tokens.Count.ShouldBe(2);
        var old = _store.Tokens.Single(t => t.TokenHash.AsSpan().SequenceEqual(RefreshTokenHasher.Hash(issued.RawToken)));
        var replacement = _store.Tokens.Single(t => t.TokenHash.AsSpan().SequenceEqual(RefreshTokenHasher.Hash(result.Value.RawToken)));
        old.RevokedAt.ShouldBe(Now);
        replacement.RevokedAt.ShouldBeNull();
        replacement.FamilyId.ShouldBe(familyId);
        replacement.DeviceLabel.ShouldBe("Pixel 8");
    }

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        var result = await _service.RotateAsync("not-a-real-token", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("refresh_token.invalid");
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task An_expired_token_is_rejected_and_retired()
    {
        var issued = await _service.IssueAsync(UserId.New(), null, CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(31));

        var result = await _service.RotateAsync(issued.RawToken, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("refresh_token.expired");
        _store.Tokens.Single().RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Reusing_a_rotated_token_is_detected_and_revokes_the_whole_family()
    {
        var issued = await _service.IssueAsync(UserId.New(), null, CancellationToken.None);

        // Legitimate rotation: the presented token is retired, a replacement issued.
        var rotated = await _service.RotateAsync(issued.RawToken, CancellationToken.None);
        rotated.IsSuccess.ShouldBeTrue();

        // The stolen, already-retired token is replayed.
        var reuse = await _service.RotateAsync(issued.RawToken, CancellationToken.None);

        reuse.IsFailure.ShouldBeTrue();
        reuse.Error.Code.ShouldBe("refresh_token.reuse_detected");

        // Every token in the family - including the otherwise-live replacement - is now revoked.
        _store.Tokens.ShouldAllBe(t => t.RevokedAt != null);
    }

    [Fact]
    public async Task A_reused_token_cannot_be_laundered_into_a_valid_session()
    {
        var issued = await _service.IssueAsync(UserId.New(), null, CancellationToken.None);
        var rotated = await _service.RotateAsync(issued.RawToken, CancellationToken.None);
        await _service.RotateAsync(issued.RawToken, CancellationToken.None); // reuse -> family revoked

        // The replacement handed out by the legitimate rotation is now dead too.
        var afterRevocation = await _service.RotateAsync(rotated.Value.RawToken, CancellationToken.None);

        afterRevocation.IsFailure.ShouldBeTrue();
        afterRevocation.Error.Code.ShouldBe("refresh_token.reuse_detected");
    }

    [Fact]
    public async Task Revoking_a_device_deletes_its_token()
    {
        var issued = await _service.IssueAsync(UserId.New(), null, CancellationToken.None);

        await _service.RevokeDeviceAsync(issued.RawToken, CancellationToken.None);

        _store.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoking_an_unknown_token_is_a_no_op()
    {
        await _service.IssueAsync(UserId.New(), null, CancellationToken.None);

        await Should.NotThrowAsync(() => _service.RevokeDeviceAsync("not-a-real-token", CancellationToken.None));

        _store.Tokens.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Revoking_all_deletes_every_token_of_that_user_and_nobody_elses()
    {
        var target = UserId.New();
        var bystander = UserId.New();
        await _service.IssueAsync(target, "Pixel 8", CancellationToken.None);
        await _service.IssueAsync(target, "Firefox on Linux", CancellationToken.None);
        await _service.IssueAsync(bystander, null, CancellationToken.None);

        await _service.RevokeAllAsync(target, CancellationToken.None);

        _store.Tokens.ShouldHaveSingleItem().UserId.ShouldBe(bystander.Value);
    }
}
