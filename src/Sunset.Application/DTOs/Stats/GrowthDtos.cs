using System.Text.Json.Serialization;

namespace Sunset.Application.DTOs.Stats;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GrowthGranularity
{
    Day,
    Week,
    Month
}

/// <summary>What can be charted. Each is counted by the date the row was created.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GrowthMetric
{
    Users,
    Locations,
    Photos,
    Ratings
}

/// <param name="From">First day wanted (Brasília time). Default: a window ending at <paramref name="To"/> (30 days, 12 weeks or 12 months).</param>
/// <param name="To">Last day wanted (Brasília time). Default: today.</param>
/// <param name="Metrics">Comma-separated metric names ("users,photos"); default: all of them.</param>
public sealed record GrowthQuery(DateOnly? From, DateOnly? To, GrowthGranularity Granularity, string? Metrics)
{
    /// <summary>One answer holds at most this many points per metric, so a chart stays cheap to build.</summary>
    public const int MaxPoints = 400;
}

/// <param name="Date">First day of the period (the Monday of a week, the 1st of a month).</param>
/// <param name="New">Rows created in the period.</param>
/// <param name="Total">Rows created up to the end of the period (cumulative, including everything before the range).</param>
public sealed record GrowthPoint(DateOnly Date, int New, int Total);

/// <summary>Where each count stands today. For users, <c>Total</c> counts every signup ever made.</summary>
public sealed record GrowthSummary(int Users, int ActiveAccounts, int RemovedAccounts, int Locations, int Photos, int Ratings);

/// <param name="Series">One list of points per requested metric, keyed by its lower-case name ("users", "locations"...).</param>
public sealed record GrowthResponse(
    GrowthGranularity Granularity,
    string TimeZone,
    DateOnly From,
    DateOnly To,
    IReadOnlyDictionary<string, IReadOnlyList<GrowthPoint>> Series,
    GrowthSummary Summary);
