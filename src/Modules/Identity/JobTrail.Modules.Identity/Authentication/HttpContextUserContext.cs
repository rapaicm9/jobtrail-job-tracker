using JobTrail.Modules.Identity.Contracts;
using JobTrail.Modules.Identity.Features;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// Resolves the current caller from the request's principal. Reuses the same
/// <c>sub</c>-claim reader the module's own endpoints use, so the token's shape
/// is understood in exactly one place; consumers across the boundary see only a
/// <see cref="UserId"/>.
/// </summary>
internal sealed class HttpContextUserContext(IHttpContextAccessor accessor) : IUserContext
{
    public UserId? UserId =>
        accessor.HttpContext?.User is { } principal && principal.TryGetId(out var userId)
            ? userId
            : null;
}
