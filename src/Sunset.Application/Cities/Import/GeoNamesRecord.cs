using System.Globalization;

namespace Sunset.Application.Cities.Import;

/// <summary>One line of a GeoNames country dump (tab separated, 19 columns).</summary>
public sealed record GeoNamesRecord(
    long GeoNameId,
    string Name,
    string AlternateNames,
    double Latitude,
    double Longitude,
    string FeatureClass,
    string FeatureCode,
    string CountryCode,
    string Admin1Code,
    string Admin2Code,
    int Population,
    string? TimeZoneId);

public static class GeoNamesParser
{
    private const int ColumnCount = 19;

    /// <summary>Null for a malformed line (wrong column count or unreadable numbers).</summary>
    public static GeoNamesRecord? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var c = line.Split('\t');
        if (c.Length != ColumnCount)
            return null;

        if (!long.TryParse(c[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || !double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
            || string.IsNullOrWhiteSpace(c[1]))
            return null;

        // The population column can be empty or exceed int on huge cities; neither matters for ranking here.
        long.TryParse(c[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population);

        return new GeoNamesRecord(
            id, c[1].Trim(), c[3], lat, lng, c[6], c[7], c[8], c[10], c[11],
            (int)Math.Clamp(population, 0, int.MaxValue),
            string.IsNullOrWhiteSpace(c[17]) ? null : c[17]);
    }
}
