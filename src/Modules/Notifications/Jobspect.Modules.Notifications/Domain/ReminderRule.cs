using Jobspect.SharedKernel;

namespace Jobspect.Modules.Notifications.Domain;

/// <summary>
/// The Pro automation: nudge me about an application that has sat in Applied this
/// long without an answer. One row per account, or none.
/// <para>
/// <b>The one thing in this module the account states rather than derives.</b>
/// Every reminder here was raised from an event or from this rule; the rule itself
/// is a number the user chose, so it is the module's own state and not a projection
/// of anyone else's.
/// </para>
/// <para>
/// There is no enabled flag. Deleting the rule is how it is turned off, which keeps
/// exactly one way to say "no automation" - the absence of the row - rather than
/// two that can disagree.
/// </para>
/// </summary>
internal sealed class ReminderRule
{
    /// <summary>
    /// A surrogate key rather than the account, even though there is one rule per
    /// account today. A raised follow-up points back at the rule that raised it, and
    /// the rule is addressable in its own right; keying on the owner would make the
    /// cap part of the identity of the row instead of a constraint over it - see
    /// <see cref="OwnerId"/>.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The account this automates for. Unique, which is where "one rule per account"
    /// actually lives: the database holds it rather than a handler remembering to
    /// look first. A second rule type would drop that index rather than reshape the
    /// table.
    /// </summary>
    public UserId OwnerId { get; set; }

    /// <summary>
    /// How many days an application may sit in Applied before the follow-up is
    /// raised. Measured from the date the user says they applied, which is their own
    /// truth about when the clock started and not when they got round to recording
    /// it.
    /// </summary>
    public int DaysAfterApplied { get; set; }

    /// <summary>When the account first set up the automation. Kept through later changes.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
