namespace JobTrail.Modules.Identity.Features.Login;

/// <summary>Credentials plus an optional label for the new refresh-token row.</summary>
internal sealed record LoginRequest(
    string? Email,
    string? Password,
    string? DeviceLabel);
