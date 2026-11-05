using Sunset.Application.DTOs.Stats;

namespace Sunset.Application.Interfaces;

public interface IStatsService
{
    /// <summary>
    /// Growth curves (new and cumulative) for the moderation dashboard, by day, week or month, in Brasília time.
    /// Periods without activity appear with zero. Raises a validation error for a bad range or an unknown metric.
    /// </summary>
    Task<GrowthResponse> GetGrowthAsync(GrowthQuery query, CancellationToken cancellationToken = default);
}
