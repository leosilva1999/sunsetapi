namespace Sunset.Application.Cities.Import;

/// <summary>
/// What counts as a "city" for one country. It differs between countries (in many, ADM2 is a county
/// and the city is a populated place), so each country the importer supports gets its own rules; only
/// Brazil exists for now.
/// </summary>
public sealed record CityImportRules(
    string Country,
    IReadOnlySet<string> MunicipalityFeatureCodes,
    IReadOnlySet<string> LocalityFeatureCodes,
    IReadOnlyDictionary<string, string> Admin1ToArea,
    bool NationalCodeIsAdmin2)
{
    // GeoNames identifies Brazilian states by a numeric admin1 code.
    private static readonly Dictionary<string, string> BrazilStates = new()
    {
        ["01"] = "AC", ["02"] = "AL", ["03"] = "AP", ["04"] = "AM", ["05"] = "BA", ["06"] = "CE", ["07"] = "DF",
        ["08"] = "ES", ["11"] = "MS", ["13"] = "MA", ["14"] = "MT", ["15"] = "MG", ["16"] = "PA", ["17"] = "PB",
        ["18"] = "PR", ["20"] = "PI", ["21"] = "RJ", ["22"] = "RN", ["23"] = "RS", ["24"] = "RO", ["25"] = "RR",
        ["26"] = "SC", ["27"] = "SP", ["28"] = "SE", ["29"] = "GO", ["30"] = "PE", ["31"] = "TO",
    };

    /// <summary>
    /// Municipality = ADM2 (its admin2 code IS the 7-digit IBGE code). Locality = populated places,
    /// leaving out PPLX (sections/neighbourhoods: too noisy).
    /// </summary>
    public static CityImportRules Brazil { get; } = new(
        "BR",
        new HashSet<string> { "ADM2" },
        new HashSet<string> { "PPL", "PPLL", "PPLA", "PPLA2", "PPLA3", "PPLA4", "PPLC" },
        BrazilStates,
        NationalCodeIsAdmin2: true);

    public static CityImportRules? For(string country) =>
        string.Equals(country, "BR", StringComparison.OrdinalIgnoreCase) ? Brazil : null;
}
