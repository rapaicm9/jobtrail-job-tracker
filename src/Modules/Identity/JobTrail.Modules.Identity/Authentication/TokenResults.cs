using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>A minted access token and the instant it expires.</summary>
internal sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>The raw refresh-token secret handed to the client, and its expiry.</summary>
internal sealed record IssuedRefreshToken(string RawToken, DateTimeOffset ExpiresAt);

/// <summary>The result of rotating a refresh token: the owning account plus the replacement secret.</summary>
internal sealed record RotatedRefreshToken(UserId UserId, string RawToken, DateTimeOffset ExpiresAt);

/// <summary>A matched access + refresh pair returned to the auth endpoints.</summary>
internal sealed record IssuedTokens(
    UserId UserId, AccessToken Access, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
