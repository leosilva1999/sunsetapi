using Sunset.Application.DTOs.Stats;

namespace Sunset.Application.Interfaces.Repositories;

public interface IStatsRepository
{
    /// <summary>
    /// Rows created per period, for the periods that have any. Periods are cut on <paramref name="timeZoneId"/> calendar
    /// days; the range runs from the start of <paramref name="firstPeriod"/> up to (not including) <paramref name="endExclusive"/>.
    /// Removed content (hidden locations and photos) is not counted.
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, int>> GetNewPerPeriodAsync(
        GrowthMetric metric, GrowthGranularity granularity, DateOnly firstPeriod, DateOnly endExclusive, string timeZoneId, CancellationToken cancellationToken = default);

    /// <summary>Rows created before the start of <paramref name="firstPeriod"/> (the baseline of the cumulative curve).</summary>
    Task<int> CountBeforeAsync(GrowthMetric metric, DateOnly firstPeriod, string timeZoneId, CancellationToken cancellationToken = default);

    Task<GrowthSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}
