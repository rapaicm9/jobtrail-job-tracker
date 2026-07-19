using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class TokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeRefreshTokenStore _store = new();
    private readonly FakeUserTokenVersionReader _versions = new();
    private readonly JwtOptions _options = TestKeys.NewOptions();
    private readonly TokenService _service;

    public TokenServiceTests()
    {
        var clock = new TestTimeProvider(Now);
        var access = new AccessTokenIssuer(new EcdsaSigningKeyProvider(_options.Wrap()), _options.Wrap(), clock);
        var refresh = new RefreshTokenService(_store, _options.Wrap(), clock);
        _service = new TokenService(access, refresh, _versions);
    }

    [Fact]
    public async Task Issuing_returns_a_matched_access_and_refresh_pair()
    {
        var userId = UserId.New();

        var tokens = await _service.IssueAsync(userId, tokenVersion: 3, "Firefox on Linux", CancellationToken.None);

        TokenVersionOf(tokens.Access.Value).ShouldBe("3");
        SubjectOf(tokens.Access.Value).ShouldBe(userId.ToString());
        _store.Tokens.ShouldHaveSingleItem().TokenHash.ShouldBe(RefreshTokenHasher.Hash(tokens.RefreshToken));
    }

    [Fact]
    public async Task Refreshing_rotates_the_token_and_mints_access_with_the_current_version()
    {
        var userId = UserId.New();
        var issued = await _service.IssueAsync(userId, tokenVersion: 3, null, CancellationToken.None);

        // A global logout happened since issue: the account's version moved on.
        _versions.Versions[userId.Value] = 4;

        var refreshed = await _service.RefreshAsync(issued.RefreshToken, CancellationToken.None);

        refreshed.IsSuccess.ShouldBeTrue();
        refreshed.Value.RefreshToken.ShouldNotBe(issued.RefreshToken);
        TokenVersionOf(refreshed.Value.Access.Value).ShouldBe("4");
    }

    [Fact]
    public async Task Refreshing_a_reused_token_fails()
    {
        var userId = UserId.New();
        _versions.Versions[userId.Value] = 0;
        var issued = await _service.IssueAsync(userId, 0, null, CancellationToken.None);

        await _service.RefreshAsync(issued.RefreshToken, CancellationToken.None);
        var reuse = await _service.RefreshAsync(issued.RefreshToken, CancellationToken.None);

        reuse.IsFailure.ShouldBeTrue();
        reuse.Error.Code.ShouldBe("refresh_token.reuse_detected");
    }

    [Fact]
    public async Task Refreshing_for_a_vanished_account_fails()
    {
        // Issued but the reader has no version for this user - the account is gone.
        var issued = await _service.IssueAsync(UserId.New(), 0, null, CancellationToken.None);

        var result = await _service.RefreshAsync(issued.RefreshToken, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("refresh_token.user_not_found");
    }

    private static string SubjectOf(string accessToken) =>
        new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Subject;

    private static string TokenVersionOf(string accessToken) =>
        new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim(AccessTokenIssuer.TokenVersionClaim).Value;
}
