namespace Jobspect.Modules.Notifications.Features.CountUnreadReminders;

/// <summary>
/// The badge figure. An object rather than a bare number so the response has somewhere
/// to grow - a bare <c>3</c> is valid JSON and a dead end.
/// </summary>
internal sealed record UnreadCountResponse(int Count);
