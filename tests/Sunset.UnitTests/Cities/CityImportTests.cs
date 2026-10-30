using Sunset.Application.Cities.Import;
using Sunset.Application.Common;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Cities;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Brasília", "brasilia")]
    [InlineData("São Tomé das Letras", "sao tome das letras")]
    [InlineData("  Santa   Helena  ", "santa helena")]
    [InlineData("Goiaminas - MG", "goiaminas mg")]
    [InlineData("Água Boa/MT", "agua boa mt")]
    [InlineData("D'Oeste", "d oeste")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("---", "")]
    public void Normalize_StripsAccentsLowercasesAndCollapsesSpaces(string? input, string expected) =>
        Assert.Equal(expected, NameNormalizer.Normalize(input));
}

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Imbituba", "SC", "imbituba-sc")]
    [InlineData("Santa Helena", "PR", "santa-helena-pr")]
    [InlineData("São Tomé das Letras", "MG", "sao-tome-das-letras-mg")]
    [InlineData("Assú", "RN", "assu-rn")]
    public void ForMunicipality_IsNameThenState(string name, string state, string expected) =>
        Assert.Equal(expected, SlugGenerator.ForMunicipality(name, state));

    [Fact]
    public void ForLocality_AppendsTheMunicipalitySlug() =>
        Assert.Equal("praia-do-rosa-imbituba-sc", SlugGenerator.ForLocality("Praia do Rosa", "imbituba-sc"));

    [Fact]
    public void Unique_ReturnsTheBaseWhenFree_ThenNumbersTheCollisions()
    {
        var taken = new HashSet<string> { "zombaria-pocao-pe", "zombaria-pocao-pe-2" };

        Assert.Equal("outro-pe", SlugGenerator.Unique("outro-pe", taken.Contains));
        Assert.Equal("zombaria-pocao-pe-3", SlugGenerator.Unique("zombaria-pocao-pe", taken.Contains));
    }

    [Fact]
    public void LongSlugs_AreCutToTheLimit_AndStayUnique()
    {
        var longName = string.Join(' ', Enumerable.Repeat("palavra", 40));
        var slug = SlugGenerator.ForMunicipality(longName, "SC");
        Assert.True(slug.Length <= SlugGenerator.MaxLength);

        var next = SlugGenerator.Unique(slug, s => s == slug);
        Assert.True(next.Length <= SlugGenerator.MaxLength);
        Assert.EndsWith("-2", next);
        Assert.NotEqual(slug, next);
    }
}

public class GeoNamesParserTests
{
    // geonameid, name, ascii, alternates, lat, lng, class, code, country, cc2, admin1, admin2, admin3, admin4, population, elevation, dem, tz, modified
    internal static string Line(long id, string name, string featureClass, string featureCode, string admin1, string admin2,
        string alternates = "", int population = 0, string tz = "America/Sao_Paulo", string country = "BR", double lat = -28.2, double lng = -48.6) =>
        string.Join('\t', id, name, name, alternates, lat.ToString(System.Globalization.CultureInfo.InvariantCulture),
            lng.ToString(System.Globalization.CultureInfo.InvariantCulture), featureClass, featureCode, country, "", admin1, admin2,
            "", "", population, "", "", tz, "2024-01-01");

    [Fact]
    public void TryParse_ReadsTheColumnsThatMatter()
    {
        var record = GeoNamesParser.TryParse(Line(6323145, "Imbituba", "A", "ADM2", "26", "4207304", "Imbituba,Embituba", 45286));

        Assert.NotNull(record);
        Assert.Equal(6323145, record!.GeoNameId);
        Assert.Equal("Imbituba", record.Name);
        Assert.Equal("Imbituba,Embituba", record.AlternateNames);
        Assert.Equal(-28.2, record.Latitude);
        Assert.Equal("ADM2", record.FeatureCode);
        Assert.Equal("26", record.Admin1Code);
        Assert.Equal("4207304", record.Admin2Code);
        Assert.Equal(45286, record.Population);
        Assert.Equal("America/Sao_Paulo", record.TimeZoneId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a geonames line")]
    [InlineData("1\tonly\tthree\tcolumns")]
    public void TryParse_RejectsMalformedLines(string line) =>
        Assert.Null(GeoNamesParser.TryParse(line));

    [Fact]
    public void TryParse_RejectsAnUnreadableCoordinate()
    {
        var bad = Line(1, "X", "P", "PPL", "26", "1").Replace("-28.2", "abc");
        Assert.Null(GeoNamesParser.TryParse(bad));
    }

    [Fact]
    public void TryParse_ToleratesAnEmptyPopulationAndTimeZone()
    {
        var parts = Line(1, "X", "P", "PPL", "26", "1").Split('\t');
        parts[14] = "";
        parts[17] = "";

        var record = GeoNamesParser.TryParse(string.Join('\t', parts));

        Assert.Equal(0, record!.Population);
        Assert.Null(record.TimeZoneId);
    }
}

public class CityImportPlannerTests
{
    private static string Line(long id, string name, string cls, string code, string a1, string a2, string alt = "", int pop = 0) =>
        GeoNamesParserTests.Line(id, name, cls, code, a1, a2, alt, pop);

    private static CityImportPlan Plan(IEnumerable<string> lines, IReadOnlyCollection<CityNameOverride>? overrides = null, IReadOnlyCollection<ManualCity>? manual = null) =>
        CityImportPlanner.Plan(lines, CityImportRules.Brazil, overrides, manual);

    [Fact]
    public void Municipalities_AreAdm2_WithStateAndNationalCode()
    {
        var plan = Plan([Line(1, "Imbituba", "A", "ADM2", "26", "4207304", pop: 45286)]);

        var city = Assert.Single(plan.Municipalities);
        Assert.Equal(CityKind.Municipality, city.Kind);
        Assert.Equal("SC", city.AdminArea);
        Assert.Equal("4207304", city.NationalCode);
        Assert.Equal("BR", city.Country);
        Assert.Equal(1, city.ExternalId);
        Assert.Equal("geonames", city.Source);
    }

    [Fact]
    public void FederalDistrict_MapsToDf()
    {
        var plan = Plan([Line(7, "Brasília", "A", "ADM2", "07", "5300108")]);

        Assert.Equal("DF", Assert.Single(plan.Municipalities).AdminArea);
    }

    [Fact]
    public void ANameOverride_ReplacesTheDisplayName_AndKeepsTheSourceName()
    {
        var plan = Plan(
            [Line(2, "Açu", "A", "ADM2", "22", "2400208")],
            [new CityNameOverride("2400208", "Assú")]);

        var city = Assert.Single(plan.Municipalities);
        Assert.Equal("Assú", city.Name);
        Assert.Equal("Açu", city.SourceName);
        Assert.Equal(1, plan.Stats.NameOverridesApplied);
        // both spellings are searchable
        Assert.Contains("assu", city.SearchText);
        Assert.Contains("acu", city.SearchText);
    }

    [Fact]
    public void Localities_AreLinkedToTheirMunicipality()
    {
        var plan = Plan(
        [
            Line(1, "Imbituba", "A", "ADM2", "26", "4207304"),
            Line(10, "Praia do Rosa", "P", "PPL", "26", "4207304"),
        ]);

        var locality = Assert.Single(plan.Localities);
        Assert.Equal(CityKind.Locality, locality.Kind);
        Assert.Equal(Assert.Single(plan.Municipalities).Key, locality.ParentKey);
        Assert.Equal("SC", locality.AdminArea);
        Assert.Null(locality.NationalCode);
    }

    [Fact]
    public void Localities_SkipSectionsNeighbourhoodsAndNonPlaces()
    {
        var plan = Plan(
        [
            Line(1, "Imbituba", "A", "ADM2", "26", "4207304"),
            Line(10, "Bairro X", "P", "PPLX", "26", "4207304"),
            Line(11, "Praia da Vila", "T", "BCH", "26", "4207304"),
            Line(12, "Vila Y", "P", "PPLL", "26", "4207304"),
        ]);

        Assert.Equal("Vila Y", Assert.Single(plan.Localities).Name);
    }

    [Fact]
    public void ALocalityWithoutAMunicipality_IsSkippedAndCounted()
    {
        var plan = Plan(
        [
            Line(1, "Imbituba", "A", "ADM2", "26", "4207304"),
            Line(10, "Perdido", "P", "PPL", "26", "9999999"),
        ]);

        Assert.Empty(plan.Localities);
        Assert.Equal(1, plan.Stats.LocalitiesWithoutMunicipality);
    }

    [Fact]
    public void TheMunicipalitysOwnSeat_IsNotAlsoALocality()
    {
        var plan = Plan(
        [
            Line(1, "Imbituba", "A", "ADM2", "26", "4207304"),
            Line(10, "Imbituba", "P", "PPL", "26", "4207304"),
            Line(11, "IMBITUBA", "P", "PPLA2", "26", "4207304"),
        ]);

        Assert.Empty(plan.Localities);
        Assert.Equal(2, plan.Stats.LocalitiesSameNameAsMunicipality);
    }

    [Fact]
    public void TheSeatCheckAlsoUsesTheSourceName_WhenTheDisplayNameWasCorrected()
    {
        var plan = Plan(
        [
            Line(1, "Açu", "A", "ADM2", "22", "2400208"),
            Line(10, "Açu", "P", "PPL", "22", "2400208"),
        ],
        [new CityNameOverride("2400208", "Assú")]);

        Assert.Empty(plan.Localities);
    }

    [Fact]
    public void HomonymousMunicipalities_AreKeptApartByState()
    {
        var plan = Plan(
        [
            Line(1, "Santa Helena", "A", "ADM2", "13", "2109809"),
            Line(2, "Santa Helena", "A", "ADM2", "18", "4123501"),
            Line(3, "Santa Helena", "A", "ADM2", "26", "4215554"),
            Line(10, "Marinas", "P", "PPL", "18", "4123501"),
        ]);

        Assert.Equal(3, plan.Municipalities.Count);
        var marinas = Assert.Single(plan.Localities);
        Assert.Equal("PR", marinas.AdminArea);
        Assert.Equal(plan.Municipalities.Single(m => m.AdminArea == "PR").Key, marinas.ParentKey);
    }

    [Fact]
    public void AlternateNames_FeedTheSearchText_WithoutAccentsOrDuplicates()
    {
        var plan = Plan(
        [
            Line(1, "Buritis", "A", "ADM2", "15", "3109303"),
            Line(10, "Goiasminas", "P", "PPL", "15", "3109303", alt: "Goiaminas,Goiasminas,Góiasminas"),
        ]);

        var locality = Assert.Single(plan.Localities);
        Assert.Equal("goiasminas goiaminas", locality.SearchText);
        Assert.Equal("Goiaminas,Goiasminas,Góiasminas", locality.AlternateNames);
    }

    [Fact]
    public void AManualCity_IsAdded_UnlessTheDatasetAlreadyHasIt()
    {
        var manual = new ManualCity("5101837", "Boa Esperança do Norte", "MT", -13.5, -55.1, "America/Cuiaba", 5877);

        var withoutIt = Plan([Line(1, "Imbituba", "A", "ADM2", "26", "4207304")], manual: [manual]);
        var added = withoutIt.Municipalities.Single(m => m.Source == "manual");
        Assert.Equal("boa esperanca do norte", added.SearchText);
        Assert.Equal("5101837", added.NationalCode);
        Assert.Null(added.ExternalId);
        Assert.Equal(1, withoutIt.Stats.ManualAdditions);

        var withIt = Plan([Line(2, "Boa Esperança do Norte", "A", "ADM2", "14", "5101837")], manual: [manual]);
        Assert.Single(withIt.Municipalities);
        Assert.Equal("geonames", withIt.Municipalities[0].Source);
        Assert.Equal(0, withIt.Stats.ManualAdditions);
    }

    [Fact]
    public void LinesFromOtherCountries_AndMalformedLines_AreIgnoredAndCounted()
    {
        var plan = Plan(
        [
            Line(1, "Imbituba", "A", "ADM2", "26", "4207304"),
            GeoNamesParserTests.Line(2, "Paris", "A", "ADM2", "11", "75", country: "FR"),
            "garbage",
        ]);

        Assert.Single(plan.Municipalities);
        Assert.Equal(1, plan.Stats.MalformedLines);
    }

    [Fact]
    public void ThePlanIsDeterministic_RegardlessOfTheOrderOfTheFile()
    {
        var lines = new[]
        {
            Line(1, "Imbituba", "A", "ADM2", "26", "4207304"),
            Line(30, "Vila Z", "P", "PPL", "26", "4207304"),
            Line(20, "Vila Z", "P", "PPL", "26", "4207304"),
        };

        var forward = Plan(lines).Localities.Select(l => l.ExternalId);
        var reversed = Plan(lines.Reverse()).Localities.Select(l => l.ExternalId);

        Assert.Equal(forward, reversed);
        Assert.Equal([20L, 30L], forward.Select(id => id!.Value));
    }

    [Fact]
    public void BuildSearchText_DeduplicatesNames()
    {
        Assert.Equal("assu acu", CityImportPlanner.BuildSearchText("Assú", "Açu", "Assu,Açu"));
    }
}
