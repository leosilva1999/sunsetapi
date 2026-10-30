namespace Sunset.Application.Common;

/// <summary>URL slugs for cities. A slug is assigned once and never regenerated (it can already be indexed).</summary>
public static class SlugGenerator
{
    public const int MaxLength = 160;

    // "Imbituba" + "SC" -> "imbituba-sc"; the state always takes part, which tells homonyms apart.
    public static string ForMunicipality(string name, string adminArea) =>
        Fit($"{Slugify(name)}-{Slugify(adminArea)}");

    // "Praia do Rosa" in "imbituba-sc" -> "praia-do-rosa-imbituba-sc"
    public static string ForLocality(string name, string parentSlug) =>
        Fit($"{Slugify(name)}-{parentSlug}");

    public static string Slugify(string? value) =>
        NameNormalizer.Normalize(value).Replace(' ', '-');

    /// <summary>First free slug: <paramref name="baseSlug"/>, then -2, -3...</summary>
    public static string Unique(string baseSlug, Func<string, bool> isTaken)
    {
        if (!isTaken(baseSlug))
            return baseSlug;

        for (var n = 2; ; n++)
        {
            var candidate = Fit(baseSlug, suffixLength: n.ToString().Length + 1) + "-" + n;
            if (!isTaken(candidate))
                return candidate;
        }
    }

    private static string Fit(string slug, int suffixLength = 0)
    {
        var max = MaxLength - suffixLength;
        return slug.Length <= max ? slug : slug[..max].TrimEnd('-');
    }
}
