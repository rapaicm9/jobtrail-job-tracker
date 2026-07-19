namespace JobTrail.Modules.Identity.Features.Logout;

/// <summary>The refresh token whose device session should end.</summary>
internal sealed record LogoutRequest(string? RefreshToken);
