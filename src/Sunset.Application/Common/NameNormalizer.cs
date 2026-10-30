using System.Globalization;
using System.Text;

namespace Sunset.Application.Common;

/// <summary>Accent-free, lower-case, single-spaced form of a name - what search and slugs are built from.</summary>
public static class NameNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && result.Length > 0)
                    result.Append(' ');
                pendingSpace = false;
                result.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return result.ToString();
    }
}
