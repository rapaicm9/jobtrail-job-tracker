namespace JobTrail.Modules.Identity.Features.Refresh;

/// <summary>The opaque refresh token to rotate.</summary>
internal sealed record RefreshRequest(string? RefreshToken);
