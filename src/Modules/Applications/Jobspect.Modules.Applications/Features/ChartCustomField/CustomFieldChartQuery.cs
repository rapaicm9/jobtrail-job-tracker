using System.Text.Json;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobspect.Modules.Applications.Features.ChartCustomField;

/// <summary>
/// Aggregates one custom field for its owner, inside the module that holds it.
/// <para>
/// <b>Only the one field's answers are read.</b> The statement projects a single
/// path out of the JSONB document rather than loading the bag, so no other field's
/// values - a free-text note, a URL - are so much as fetched while a chart is
/// drawn. That is a narrower guarantee than the boundary alone would give, and it
/// is worth having: the reason this query exists at all is to keep user-authored
/// text from travelling.
/// </para>
/// <para>
/// The answers are then coerced and counted in memory. There is no index to lean
/// on here - the <c>jsonb_path_ops</c> GIN index serves containment, which is what
/// filtering by an exact value needs and what aggregating every value for a path
/// cannot use - so this is a scan either way, and doing the arithmetic in code
/// makes it something a test can drive.
/// </para>
/// </summary>
internal sealed class CustomFieldChartQuery(ApplicationsDbContext dbContext) : ICustomFieldChartQuery
{
    public async Task<CustomFieldChart?> GetChartAsync(
        UserId ownerId, Guid definitionId, Guid? campaignId, CancellationToken cancellationToken)
    {
        // Ownership is the lookup. A definition belonging to somebody else simply
        // is not found, and an unchartable one is not found either - the caller
        // learns nothing about ids that are not theirs.
        var definition = await dbContext.CustomFields
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId && d.OwnerId == ownerId, cancellationToken);

        if (definition is null || !CustomFieldAnswerBuckets.IsChartable(definition.Type))
        {
            return null;
        }

        var answers = await AnswersAsync(ownerId, definitionId, campaignId, cancellationToken);

        return CustomFieldAnswerBuckets.Build(definition, answers);
    }

    /// <summary>
    /// This field's answer on each of the owner's applications, in the order the
    /// database returns them - order is meaningless to every figure built from
    /// them. An application that never answered yields null, and is still counted,
    /// because "how many did not answer" is part of the chart.
    /// </summary>
    private async Task<IReadOnlyList<JsonElement?>> AnswersAsync(
        UserId ownerId, Guid definitionId, Guid? campaignId, CancellationToken cancellationToken)
    {
        var sql = """
            SELECT (a.custom_field_values -> @definition_id::text)::text AS "Value"
            FROM applications.applications AS a
            WHERE a.owner_id = @owner_id
              AND (@campaign_id IS NULL OR a.campaign_id = @campaign_id)
            """;

        var raw = await dbContext.Database
            .SqlQueryRaw<string?>(
                sql,
                new NpgsqlParameter("owner_id", NpgsqlDbType.Uuid) { Value = ownerId.Value },
                new NpgsqlParameter("definition_id", NpgsqlDbType.Uuid) { Value = definitionId },
                new NpgsqlParameter("campaign_id", NpgsqlDbType.Uuid)
                {
                    Value = (object?)campaignId ?? DBNull.Value,
                })
            .ToListAsync(cancellationToken);

        return [.. raw.Select(Parse)];
    }

    /// <summary>
    /// One stored answer as JSON. Anything unreadable counts as unanswered: the
    /// bag holds whatever was written against a definition, and a chart that threw
    /// on one odd value would take the whole panel down with it.
    /// </summary>
    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind is JsonValueKind.Null
                ? null
                : document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
