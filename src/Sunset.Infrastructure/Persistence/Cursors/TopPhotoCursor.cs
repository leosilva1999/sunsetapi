using System.Globalization;
using System.Text;

namespace Sunset.Infrastructure.Persistence.Cursors;

internal static class TopPhotoCursor
{
    public static string Encode(int likesCount, DateTime createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{likesCount}|{createdAt:O}|{id}"));

    public static (int LikesCount, DateTime CreatedAt, Guid Id)? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 3)
                return null;

            var likesCount = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var createdAt = DateTime.Parse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var id = Guid.Parse(parts[2]);
            return (likesCount, createdAt, id);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
