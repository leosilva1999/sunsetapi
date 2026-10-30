using System.Globalization;
using System.Text;

namespace Sunset.Infrastructure.Persistence.Cursors;

/// <summary>Cursor of the "most populous first" order: (population desc, id).</summary>
internal static class CityPopulationCursor
{
    public static string Encode(int population, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{population.ToString(CultureInfo.InvariantCulture)}|{id}"));

    public static (int Population, Guid Id)? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return parts.Length == 2
                ? (int.Parse(parts[0], CultureInfo.InvariantCulture), Guid.Parse(parts[1]))
                : null;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return null;
        }
    }
}

/// <summary>Cursor of the alphabetical order: (name, id). The name is Base64-wrapped so any character is safe.</summary>
internal static class CityNameCursor
{
    public static string Encode(string name, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Convert.ToBase64String(Encoding.UTF8.GetBytes(name))}|{id}"));

    public static (string Name, Guid Id)? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return parts.Length == 2
                ? (Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])), Guid.Parse(parts[1]))
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
