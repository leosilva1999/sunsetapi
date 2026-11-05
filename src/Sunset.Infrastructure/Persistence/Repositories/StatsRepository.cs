using Microsoft.EntityFrameworkCore;
using Sunset.Application.DTOs.Stats;
using Sunset.Application.Interfaces.Repositories;

namespace Sunset.Infrastructure.Persistence.Repositories;

/// <summary>
/// Counts by creation date with plain SQL: the periods are cut on calendar days of a time zone, which EF cannot express.
/// Only table names and the granularity (both from fixed lists, never from the request text) are put into the SQL; every
/// value is a parameter. Raw SQL skips the soft-delete query filters, so hidden locations and photos are excluded by hand.
/// </summary>
public class StatsRepository(SunsetDbContext context) : IStatsRepository
{
    private sealed record PeriodCount(DateOnly Period, int Count);

    private static string Table(GrowthMetric metric) => metric switch
    {
        GrowthMetric.Users => "users",
        GrowthMetric.Locations => "locations",
        GrowthMetric.Photos => "photos",
        GrowthMetric.Ratings => "ratings",
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };

    // Locations and photos are soft-deleted by moderation; users (anonymized, never removed) and ratings are not.
    private static string Visible(GrowthMetric metric) =>
        metric is GrowthMetric.Locations or GrowthMetric.Photos ? "AND \"DeletedAt\" IS NULL" : string.Empty;

    private static string Unit(GrowthGranularity granularity) => granularity switch
    {
        GrowthGranularity.Week => "week",
        GrowthGranularity.Month => "month",
        _ => "day",
    };

    public async Task<IReadOnlyDictionary<DateOnly, int>> GetNewPerPeriodAsync(
        GrowthMetric metric, GrowthGranularity granularity, DateOnly firstPeriod, DateOnly endExclusive, string timeZoneId, CancellationToken cancellationToken = default)
    {
        // {0} = time zone, {1} = first day, {2} = day after the last: SqlQueryRaw turns them into parameters.
        const string template = """
            SELECT date_trunc('@UNIT@', "CreatedAt" AT TIME ZONE {0})::date AS "Period", count(*)::int AS "Count"
            FROM @TABLE@
            WHERE "CreatedAt" >= (({1}::date)::timestamp AT TIME ZONE {0})
              AND "CreatedAt" <  (({2}::date)::timestamp AT TIME ZONE {0})
              @VISIBLE@
            GROUP BY 1
            ORDER BY 1
            """;
        var sql = template.Replace("@UNIT@", Unit(granularity)).Replace("@TABLE@", Table(metric)).Replace("@VISIBLE@", Visible(metric));

        var rows = await context.Database.SqlQueryRaw<PeriodCount>(sql, timeZoneId, firstPeriod, endExclusive).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Period, r => r.Count);
    }

    public async Task<int> CountBeforeAsync(GrowthMetric metric, DateOnly firstPeriod, string timeZoneId, CancellationToken cancellationToken = default)
    {
        const string template = """
            SELECT count(*)::int AS "Value"
            FROM @TABLE@
            WHERE "CreatedAt" < (({0}::date)::timestamp AT TIME ZONE {1})
              @VISIBLE@
            """;
        var sql = template.Replace("@TABLE@", Table(metric)).Replace("@VISIBLE@", Visible(metric));

        return await context.Database.SqlQueryRaw<int>(sql, firstPeriod, timeZoneId).SingleAsync(cancellationToken);
    }

    public async Task<GrowthSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var users = await context.Users.CountAsync(cancellationToken);
        var removed = await context.Users.CountAsync(u => u.Email.EndsWith("@sunset.invalid"), cancellationToken);

        return new GrowthSummary(
            users, users - removed, removed,
            await context.Locations.CountAsync(cancellationToken),
            await context.Photos.CountAsync(cancellationToken),
            await context.Ratings.CountAsync(cancellationToken));
    }
}
