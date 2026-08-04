using Jobspect.SharedKernel;

namespace Jobspect.Modules.Notifications.Features.ScanFollowUps;

/// <summary>
/// An application that is still waiting for an answer, belonging to an account that
/// asked to be told when one waits too long. Whether it actually <em>has</em> waited
/// too long is not settled here: that needs the owner's timezone, which the database
/// does not hold.
/// </summary>
/// <param name="ApplicationId">What the follow-up will be about, and what the feed deep-links to.</param>
/// <param name="OwnerId">Who to tell, and whose local calendar decides the rest.</param>
/// <param name="AppliedDate">The day the wait started, as the owner recorded it.</param>
/// <param name="RuleId">The automation that will have raised it.</param>
/// <param name="DaysAfterApplied">How long that automation says is too long.</param>
internal sealed record FollowUpCandidate(
    Guid ApplicationId,
    UserId OwnerId,
    DateOnly AppliedDate,
    Guid RuleId,
    int DaysAfterApplied);
