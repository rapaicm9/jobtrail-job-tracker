namespace JobTrail.Modules.Applications.Features.AddNote;

/// <summary>
/// Shape-level checks on an add-note request, keyed by field. A note is the whole
/// point of the write, so an empty one is refused rather than stored blank. That
/// the application is the caller's own is the handler's job.
/// </summary>
internal static class AddNoteRequestValidator
{
    public static Dictionary<string, string[]>? Validate(AddNoteRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.Note))
        {
            errors.Add("note", "A note is required.");
        }
        else if (request.Note.Length > FieldRules.NotesMaxLength)
        {
            errors.Add("note", $"The note must be {FieldRules.NotesMaxLength} characters or fewer.");
        }

        return errors.ToResultOrNull();
    }
}
