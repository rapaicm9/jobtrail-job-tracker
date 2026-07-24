using JobTrail.Modules.Applications.Domain;
using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Verifies that the application and company a contact points at are the caller's
/// own, turning a stranger's (or missing) id into the contact-scoped validation
/// error the slice returns as a 422. Shared by create and update, since both
/// attach the same two links.
/// </summary>
internal sealed class ContactLinkGuard(OwnershipGuard ownership)
{
    public async Task<Error?> CheckAsync(
        UserId ownerId, Guid? applicationId, Guid? companyId, CancellationToken cancellationToken)
    {
        if (applicationId is { } appId && !await ownership.OwnsApplicationAsync(ownerId, appId, cancellationToken))
        {
            return ContactErrors.UnknownApplication(appId);
        }

        if (companyId is { } companyIdValue && !await ownership.OwnsCompanyAsync(ownerId, companyIdValue, cancellationToken))
        {
            return ContactErrors.UnknownCompany(companyIdValue);
        }

        return null;
    }
}
