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

    public const int LocationMaxLength = 200;

    /// <summary>Lower-case letters, digits and single hyphens only, up to the location limit: cheap guard before a lookup.</summary>
    public static bool IsWellFormed(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= LocationMaxLength
        && value.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-');

    /// <summary>"Praia do Rosa" in "imbituba-sc" -> "praia-do-rosa-imbituba-sc"; without a city just the name.</summary>
    public static string ForLocation(string name, string? citySlug)
    {
        var slug = Slugify(name);
        if (slug.Length == 0)
            slug = "local";

        return Fit(string.IsNullOrEmpty(citySlug) ? slug : $"{slug}-{citySlug}", LocationMaxLength);
    }

    /// <summary>The nth variant of a taken slug: <paramref name="baseSlug"/>-2, -3...</summary>
    public static string WithSuffix(string baseSlug, int n, int maxLength = MaxLength) =>
        Fit(baseSlug, maxLength, suffixLength: n.ToString().Length + 1) + "-" + n;

    /// <summary>First free slug: <paramref name="baseSlug"/>, then -2, -3...</summary>
    public static string Unique(string baseSlug, Func<string, bool> isTaken, int maxLength = MaxLength)
    {
        if (!isTaken(baseSlug))
            return baseSlug;

        for (var n = 2; ; n++)
        {
            var candidate = WithSuffix(baseSlug, n, maxLength);
            if (!isTaken(candidate))
                return candidate;
        }
    }

    private static string Fit(string slug, int maxLength = MaxLength, int suffixLength = 0)
    {
        var max = maxLength - suffixLength;
        return slug.Length <= max ? slug : slug[..max].TrimEnd('-');
    }
}
