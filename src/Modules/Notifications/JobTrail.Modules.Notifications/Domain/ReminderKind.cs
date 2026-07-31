namespace JobTrail.Modules.Notifications.Domain;

/// <summary>
/// What a reminder is for, and how far ahead of it fires. One value per line of
/// the reminder table: an interview is announced twice, an application deadline
/// twice, an offer decision three times.
/// <para>
/// The lead time is part of the value rather than a column beside it, because a
/// reminder is identified by the thing it is about <em>and</em> when it fires -
/// the morning-before and the hour-before are two reminders for one interview,
/// and a shape that could not tell them apart would let one overwrite the other.
/// </para>
/// <para>
/// Stored as its name, like every other enum in this system: the row reads for
/// itself, and reordering this list cannot silently re-point a stored reminder at
/// a different moment.
/// </para>
/// </summary>
internal enum ReminderKind
{
    /// <summary>11:00 the day before the interview, in the owner's timezone.</summary>
    InterviewMorningBefore,

    /// <summary>An hour before the interview. Relative, so it needs no clock of its own.</summary>
    InterviewHourBefore,

    /// <summary>11:00 three days before the posting's deadline.</summary>
    ApplicationDeadlineThreeDaysBefore,

    /// <summary>11:00 on the day the posting's deadline falls.</summary>
    ApplicationDeadlineMorningOf,

    /// <summary>11:00 three days before the offer has to be answered.</summary>
    OfferDecisionThreeDaysBefore,

    /// <summary>11:00 the day before the offer has to be answered.</summary>
    OfferDecisionDayBefore,

    /// <summary>11:00 on the day the offer has to be answered.</summary>
    OfferDecisionMorningOf,

    /// <summary>
    /// The Pro automation: this application has sat in Applied for the rule's
    /// number of days without a response. The only kind raised by a rule rather
    /// than by a date the user entered, and the only one whose row carries a
    /// <c>RuleId</c>.
    /// </summary>
    FollowUp,
}
