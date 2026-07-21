using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Contracts;

/// <summary>
/// The authenticated caller's account id for the current request. Other modules
/// depend on this to learn who is acting without parsing Identity's tokens - the
/// claim shape stays Identity's secret behind the boundary.
/// <para>
/// Null when the request is unauthenticated or its token carries no usable
/// subject, so a consumer decides for itself whether the absence is an error.
/// </para>
/// </summary>
public interface IUserContext
{
    UserId? UserId { get; }
}
