namespace Jobspect.Modules.Identity.Authentication;

/// <summary>
/// Token-model configuration, bound from the <c>Identity:Jwt</c> section. The PEM
/// keys are secrets, injected by the AppHost as parameters it resolves from a
/// user-secrets store locally and from the deployment host's environment when
/// published (ADR-0003) - they are never committed.
/// </summary>
internal sealed class JwtOptions
{
    public const string SectionName = "Identity:Jwt";

    /// <summary>Token issuer; validated on every access token.</summary>
    public string Issuer { get; set; } = "jobspect";

    /// <summary>Intended audience; validated on every access token.</summary>
    public string Audience { get; set; } = "jobspect-api";

    /// <summary>Access-token lifetime - kept short (5-15 min) per ADR-0003.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Refresh-token lifetime; a rotated token starts a fresh window.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>PEM-encoded ES256 (P-256) private key; signs access tokens.</summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>PEM-encoded ES256 public key; validates access tokens.</summary>
    public string PublicKeyPem { get; set; } = string.Empty;
}
