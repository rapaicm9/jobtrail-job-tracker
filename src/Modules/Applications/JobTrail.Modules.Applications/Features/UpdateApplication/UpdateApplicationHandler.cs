using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.UpdateApplication;

/// <summary>
/// Replaces the editable fields of one of the caller's applications. Ownership is
/// the query, so another user's application is a 404. The pipeline stage is left
/// untouched - it moves only through the transition endpoint - and the
/// offer-decision deadline is refused unless the application is at
/// <see cref="Stage.Offer"/>, since a decision deadline without an offer is
/// meaningless. Company follows the same reference-or-create resolution as create.
/// </summary>
internal sealed class UpdateApplicationHandler(
    ApplicationsDbContext dbContext,
    CompanyResolver companyResolver,
    TimeProvider timeProvider)
{
    public async Task<Result<ApplicationResponse>> HandleAsync(
        UserId ownerId, Guid id, UpdateApplicationRequest request, CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications
            .FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(id);
        }

        if (request.OfferDecisionDeadline is not null && application.Stage != Stage.Offer)
        {
            return ApplicationErrors.OfferDeadlineRequiresOffer;
        }

        var company = await companyResolver.ResolveAsync(
            ownerId, request.CompanyId, request.CompanyName, cancellationToken);
        if (company.IsFailure)
        {
            return company.Error;
        }

        application.CompanyId = company.Value;
        application.Role = request.Role!.Trim();
        application.Compensation = ApplicationFieldMapping.ToMoney(request.Compensation);
        application.Location = ApplicationFieldMapping.Clean(request.Location);
        application.WorkMode = ApplicationFieldMapping.ParseWorkMode(request.WorkMode);
        application.PostingUrl = ApplicationFieldMapping.Clean(request.PostingUrl);
        application.Source = ApplicationFieldMapping.Clean(request.Source);
        application.AppliedDate = request.AppliedDate!.Value;
        application.ApplicationDeadline = request.ApplicationDeadline;
        application.OfferDecisionDeadline = request.OfferDecisionDeadline;
        application.CvLabel = ApplicationFieldMapping.Clean(request.CvLabel);
        application.CoverLetterLabel = ApplicationFieldMapping.Clean(request.CoverLetterLabel);
        application.UpdatedAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        return application.ToResponse();
    }
}
