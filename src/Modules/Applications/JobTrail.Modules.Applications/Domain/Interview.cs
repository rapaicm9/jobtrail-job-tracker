using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>What kind of interview round this is.</summary>
internal enum InterviewType
{
    PhoneScreen,
    Technical,
    Behavioural,
    Onsite,
    Other,
}

/// <summary>How the round is held.</summary>
internal enum InterviewFormat
{
    Remote,
    Onsite,
    Phone,
}

/// <summary>Where a round ended up. A freshly-scheduled interview is <see cref="Pending"/>.</summary>
internal enum InterviewOutcome
{
    Pending,
    Passed,
    Failed,
    Cancelled,
}

/// <summary>
/// One interview round on an application: when it is scheduled, its kind and
/// format, how it turned out, and any notes. A round is created as
/// <see cref="InterviewOutcome.Pending"/> and its outcome recorded later. It is a
/// child of its <see cref="ApplicationId"/> and cascade-deleted with it; the
/// scheduled instant will later drive interview reminders and time-to-interview
/// analytics (through an event, once the outbox exists). <see cref="OwnerId"/> is a
/// non-FK reference to an Identity account - no cross-schema foreign key, ever.
/// </summary>
internal sealed class Interview
{
    public Guid Id { get; set; }

    public UserId OwnerId { get; set; }

    public Guid ApplicationId { get; set; }

    /// <summary>The instant the round is scheduled for (UTC on the wire).</summary>
    public DateTimeOffset ScheduledAt { get; set; }

    public InterviewType Type { get; set; }

    public InterviewFormat Format { get; set; }

    public InterviewOutcome Outcome { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the interview is next modified; null until then.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
