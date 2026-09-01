using System.Globalization;
using System.Text;

namespace Sunset.Infrastructure.Persistence.Cursors;

internal static class CreatedAtCursor
{
    public static string Encode(DateTime createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt:O}|{id}"));

    public static (DateTime CreatedAt, Guid Id)? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2)
                return null;

            var createdAt = DateTime.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var id = Guid.Parse(parts[1]);
            return (createdAt, id);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
