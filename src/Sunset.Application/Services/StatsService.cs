using FluentValidation;
using FluentValidation.Results;
using Sunset.Application.DTOs.Stats;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;

namespace Sunset.Application.Services;

public class StatsService(IStatsRepository statsRepository) : IStatsService
{
    /// <summary>Periods are cut on Brazilian calendar days: a signup at 23:30 in Brasília belongs to that day, not the next.</summary>
    public const string TimeZoneId = "America/Sao_Paulo";

    public async Task<GrowthResponse> GetGrowthAsync(GrowthQuery query, CancellationToken cancellationToken = default)
    {
        var metrics = ParseMetrics(query.Metrics);
        var granularity = query.Granularity;

        var to = query.To ?? Today();
        var from = query.From ?? DefaultFrom(to, granularity);
        if (from > to)
            throw Invalid("from", "'from' must not be after 'to'.");

        var first = PeriodStart(from, granularity);
        var last = PeriodStart(to, granularity);
        var starts = new List<DateOnly>();
        for (var day = first; day <= last; day = Next(day, granularity))
        {
            starts.Add(day);
            if (starts.Count > GrowthQuery.MaxPoints)
                throw Invalid("to", $"The range holds more than {GrowthQuery.MaxPoints} {granularity.ToString().ToLowerInvariant()}s; narrow it or use a coarser granularity.");
        }

        var endExclusive = Next(last, granularity);
        var series = new Dictionary<string, IReadOnlyList<GrowthPoint>>();
        foreach (var metric in metrics)
        {
            var perPeriod = await statsRepository.GetNewPerPeriodAsync(metric, granularity, first, endExclusive, TimeZoneId, cancellationToken);
            var running = await statsRepository.CountBeforeAsync(metric, first, TimeZoneId, cancellationToken);

            var points = new List<GrowthPoint>(starts.Count);
            foreach (var start in starts)
            {
                var added = perPeriod.GetValueOrDefault(start);
                running += added;
                points.Add(new GrowthPoint(start, added, running));
            }

            series[metric.ToString().ToLowerInvariant()] = points;
        }

        return new GrowthResponse(granularity, TimeZoneId, from, to, series, await statsRepository.GetSummaryAsync(cancellationToken));
    }

    private static List<GrowthMetric> ParseMetrics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Enum.GetValues<GrowthMetric>().ToList();

        var metrics = new List<GrowthMetric>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<GrowthMetric>(part, ignoreCase: true, out var metric) || !Enum.IsDefined(metric))
                throw Invalid("metrics", $"Unknown metric '{part}'. Use: {string.Join(", ", Enum.GetNames<GrowthMetric>().Select(n => n.ToLowerInvariant()))}.");
            if (!metrics.Contains(metric))
                metrics.Add(metric);
        }

        return metrics.Count == 0 ? Enum.GetValues<GrowthMetric>().ToList() : metrics;
    }

    private static DateOnly Today()
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");   // Brazil has had no daylight saving since 2019
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }

    private static DateOnly DefaultFrom(DateOnly to, GrowthGranularity granularity) => granularity switch
    {
        GrowthGranularity.Week => to.AddDays(-7 * 11),
        GrowthGranularity.Month => to.AddMonths(-11),
        _ => to.AddDays(-29),
    };

    /// <summary>The first day of the period holding <paramref name="day"/>: itself, that week's Monday, or the 1st.</summary>
    public static DateOnly PeriodStart(DateOnly day, GrowthGranularity granularity) => granularity switch
    {
        GrowthGranularity.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
        GrowthGranularity.Month => new DateOnly(day.Year, day.Month, 1),
        _ => day,
    };

    private static DateOnly Next(DateOnly start, GrowthGranularity granularity) => granularity switch
    {
        GrowthGranularity.Week => start.AddDays(7),
        GrowthGranularity.Month => start.AddMonths(1),
        _ => start.AddDays(1),
    };

    private static ValidationException Invalid(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}
