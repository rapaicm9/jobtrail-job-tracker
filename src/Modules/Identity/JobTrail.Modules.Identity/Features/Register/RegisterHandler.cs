using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Persistence;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Identity.Features.Register;

/// <summary>
/// Opens the account and signs the user straight in - registration hands back
/// the same token pair a login would, so the client never makes two calls.
/// </summary>
internal sealed class RegisterHandler(
    UserManager<ApplicationUser> userManager,
    IdentityModuleDbContext dbContext,
    TokenService tokenService)
{
    public async Task<Result<AuthTokensResponse>> HandleAsync(
        RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            // No separate username concept: the email is the login.
            UserName = request.Email,
            Email = request.Email,
            TimeZoneId = request.TimeZoneId ?? "Etc/UTC",
        };

        // Announced before the account is written, and deliberately so. Identity's
        // store saves through this same context, so the row goes to the database in
        // the same SaveChanges as the user - the account and the announcement of it
        // commit together or not at all, which is the whole point. Recording it
        // afterwards would leave the gap this exists to close: an account created,
        // the announcement lost to a crash, and a user who can never file an
        // application because no campaign was ever made for them.
        //
        // The key is minted in the entity's constructor rather than by the database,
        // so it is already known here.
        var announcement = dbContext.Outbox.Add(OutboxMessage.For(
            new UserRegistered(Guid.CreateVersion7(), UserId.From(user.Id))));

        var created = await userManager.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            // Identity's validators reject before the store is reached, so nothing
            // was written - but the row above is still tracked, and a handler that
            // leaves one behind is a handler that depends on nobody saving after
            // it. Drop it: a rejected registration announces nothing.
            announcement.State = EntityState.Detached;
            return ToError(created);
        }

        var tokens = await tokenService.IssueAsync(
            UserId.From(user.Id), user.TokenVersion, request.DeviceLabel, cancellationToken);

        return tokens.ToResponse();
    }

    private static Error ToError(IdentityResult result)
    {
        // The unique-email constraint is the truth; surfacing it is a 409, not a
        // validation problem - the request was well-formed, the address is taken.
        if (result.Errors.Any(e =>
                e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                    or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return Error.Conflict("registration.email_taken", "An account with this email already exists.");
        }

        // Anything else is Identity's own validators disagreeing (password
        // policy, email shape). The request validator mirrors those rules, so
        // reaching this path is rare - but map it faithfully when it happens.
        var detail = string.Join(" ", result.Errors.Select(e => e.Description));
        return Error.Validation("registration.invalid", detail);
    }
}
