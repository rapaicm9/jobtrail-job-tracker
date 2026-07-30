namespace JobTrail.Modules.Analytics.Features;

/// <summary>
/// The account's weekly application goal and how far into it this week has got.
/// </summary>
/// <param name="Target">
/// Applications the account means to send this week, or <c>null</c> when no goal is
/// set.
/// </param>
/// <param name="Applied">
/// How many the account has applied to inside the current week - and <c>null</c>,
/// not zero, when there is no goal.
/// <para>
/// Withholding the count is deliberate. Progress is progress <em>toward</em>
/// something; with no target it is a bare weekly application count, which is a paid
/// figure the trend on <c>/analytics/insights</c> already sells. An account that
/// once set a goal keeps both its target and its progress after a downgrade,
/// because that is its own record; an account that never set one has nothing here
/// to withhold.
/// </para>
/// </param>
/// <param name="WeekStart">
/// The Monday the current week began on, in the caller's own timezone. Returned
/// because no client can compute it: it depends on the timezone this server holds
/// for the user and on where this module cuts a week.
/// </param>
internal sealed record WeeklyGoalResponse(int? Target, int? Applied, DateOnly WeekStart);
