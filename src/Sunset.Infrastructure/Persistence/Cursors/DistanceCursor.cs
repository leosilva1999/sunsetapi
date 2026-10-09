using System.Globalization;
using System.Text;

namespace Sunset.Infrastructure.Persistence.Cursors;

internal static class DistanceCursor
{
    // "R" round-trips the double exactly, so the next page's `distance > cursor` comparison sees the
    // same value MySQL computed for the last row of the previous page.
    public static string Encode(double distanceKm, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{distanceKm.ToString("R", CultureInfo.InvariantCulture)}|{id}"));

    public static (double DistanceKm, Guid Id)? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2)
                return null;

            var distanceKm = double.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture);
            var id = Guid.Parse(parts[1]);
            return (distanceKm, id);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
