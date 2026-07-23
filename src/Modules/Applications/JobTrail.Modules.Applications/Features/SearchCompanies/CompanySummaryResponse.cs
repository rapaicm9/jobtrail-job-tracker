namespace JobTrail.Modules.Applications.Features.SearchCompanies;

/// <summary>
/// A company as the picker needs it: the id to reference and the name to show.
/// Deliberately narrow - website and notes stay out of the type-ahead list, so
/// the read surface can't widen by accident.
/// </summary>
internal sealed record CompanySummaryResponse(Guid Id, string Name);
