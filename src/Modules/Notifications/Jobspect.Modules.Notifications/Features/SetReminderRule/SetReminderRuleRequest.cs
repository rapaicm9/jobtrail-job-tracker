namespace Jobspect.Modules.Notifications.Features.SetReminderRule;

/// <summary>
/// How long an application may go unanswered before this account wants a nudge.
/// <para>
/// Nullable so an omitted field is a validation failure rather than a silent zero,
/// and required rather than defaulted for a reason worth stating: this one request
/// both creates the rule and changes it, so an absent value would have to mean the
/// default on the way in and "leave it alone" thereafter. Two meanings for one
/// absence is what a client discovers by having its thirty days quietly reset to
/// seven. The default belongs in the form the user is shown -
/// <see cref="SetReminderRuleRequestValidator.DefaultDaysAfterApplied"/> is where
/// that number lives.
/// </para>
/// </summary>
internal sealed record SetReminderRuleRequest(int? DaysAfterApplied);
