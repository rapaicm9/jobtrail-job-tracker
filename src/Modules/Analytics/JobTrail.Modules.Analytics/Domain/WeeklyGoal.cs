using JobTrail.SharedKernel;

namespace JobTrail.Modules.Analytics.Domain;

/// <summary>
/// How many applications an account means to send in a week. One row per account,
/// or none.
/// <para>
/// <b>The one thing in this module nobody derived.</b> Every other row here was
/// assembled from an event, which is what makes the rest of the read model
/// recomputable in principle and disposable in practice. This is a number the user
/// typed. It is not rebuildable from anything, no event carries it, and losing it
/// loses it - so it is the module's own state to keep, not a projection of
/// somebody else's.
/// </para>
/// <para>
/// The goal is account-wide and carries no campaign, unlike the figures beside it
/// on the dashboard. A target of eight measured against one campaign's slice is a
/// number the user never set; the goal is a habit, and the habit spans the search.
/// </para>
/// </summary>
internal sealed class WeeklyGoal
{
    /// <summary>
    /// The account whose goal this is, and the key. There is no surrogate id: the
    /// account <em>is</em> the identity of the row, so making it the primary key is
    /// what enforces "one goal per account" in the database rather than in a
    /// handler that could forget.
    /// </summary>
    public UserId OwnerId { get; set; }

    /// <summary>
    /// Applications per week. Always a real target - clearing the goal deletes the
    /// row, so there is exactly one way to say "no goal" and it is the absence of
    /// this row rather than a zero stored in it.
    /// </summary>
    public int Target { get; set; }

    /// <summary>When the account first set a goal. Kept through later changes to it.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the target last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
