using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Identity.Features;
using Jobspect.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace Jobspect.Modules.Identity.Authentication;

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
