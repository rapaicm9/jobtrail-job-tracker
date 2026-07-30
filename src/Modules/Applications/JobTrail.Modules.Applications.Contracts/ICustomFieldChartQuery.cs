using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Contracts;

/// <summary>
/// Aggregates one of an account's custom fields into figures another module can
/// chart.
/// <para>
/// This module's first published query rather than an event, and the exception is
/// deliberate. No event carries a custom-field answer and none ever will: the
/// label, the options and the answers are the most user-authored data in the
/// product, and routing them onto the stream would put arbitrary text into every
/// consumer's store. So the aggregation happens here, where the data and the
/// per-type coercion already are, and only figures leave.
/// </para>
/// <para>
/// <b>Buckets, not answers.</b> Individual values never cross the boundary - what
/// is returned is already counted or summarised. Text and URL fields are offered
/// nothing at all, which is a safety property rather than a scope decision: free
/// text has no route out of this module even by mistake.
/// </para>
/// </summary>
public interface ICustomFieldChartQuery
{
    /// <summary>
    /// The chart for one of <paramref name="ownerId"/>'s field definitions,
    /// optionally narrowed to one campaign.
    /// <para>
    /// Null when there is no chartable field of that id for this owner - covering
    /// a definition that does not exist, one belonging to somebody else, and one
    /// whose type is not chartable. They are one answer on purpose: ownership
    /// lives inside the query here as everywhere else, and distinguishing "not
    /// chartable" from "not yours" would confirm that an id the caller does not
    /// own exists.
    /// </para>
    /// </summary>
    Task<CustomFieldChart?> GetChartAsync(
        UserId ownerId, Guid definitionId, Guid? campaignId, CancellationToken cancellationToken);
}

/// <summary>
/// One custom field, counted. Exactly one of the three sections is populated,
/// chosen by <see cref="Type"/>.
/// <para>
/// Nullable sections rather than a type hierarchy: a contract crossing a module
/// boundary is better as data a consumer can read without a discriminator than as
/// a polymorphic shape both ends have to agree how to serialize.
/// </para>
/// </summary>
/// <param name="Applications">
/// How many of the owner's applications were considered - the denominator for any
/// percentage. Stated rather than left to be inferred from the counts, which for a
/// multi-select do not sum to it.
/// </param>
public sealed record CustomFieldChart(
    Guid DefinitionId,
    string Label,
    string Type,
    int Applications,
    IReadOnlyList<CategoryBucket>? Categories,
    NumberSummary? Numbers,
    IReadOnlyList<PeriodBucket>? Periods);

/// <summary>
/// How many applications answered with one option.
/// <para>
/// A null <see cref="Value"/> is the applications that did not answer at all, and
/// it is included: leaving it out would give a chart that quietly fails to account
/// for every application beside it.
/// </para>
/// <para>
/// For a multi-select an application answering three options lands in three
/// buckets, so these counts sum to <em>more</em> than the applications considered.
/// That is why the denominator travels separately.
/// </para>
/// </summary>
public sealed record CategoryBucket(string? Value, int Count);

/// <summary>
/// A number field summarised: how many answered, and the five values a box plot
/// is drawn from.
/// <para>
/// Deliberately not a histogram. Bin boundaries are a presentation decision, and
/// any set chosen here is one the chart cannot undo; quartiles carry the shape of
/// the distribution without committing the caller to a bin count it did not pick.
/// </para>
/// </summary>
public sealed record NumberSummary(
    int Answered, decimal Minimum, decimal LowerQuartile, decimal Median, decimal UpperQuartile, decimal Maximum);

/// <summary>How many applications answered with a date in one month, keyed by the first of it.</summary>
public sealed record PeriodBucket(DateOnly PeriodStart, int Count);
