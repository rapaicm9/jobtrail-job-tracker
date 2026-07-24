namespace JobTrail.Modules.Applications.Features.AddNote;

/// <summary>
/// The note to add to an application's timeline. The application it belongs to
/// comes from the route, and the time it happened is the write itself - a note
/// records what the user has just learned or done.
/// </summary>
internal sealed record AddNoteRequest(string? Note);
