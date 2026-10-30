using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sunset.Application.Cities.Import;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Cities;
using Sunset.Infrastructure.Persistence;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class CityImportServiceTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    // The real GeoNames lines in Data/ say "BR"; the suite runs them as a made-up country so nothing
    // here collides with other tests (slugs are global, so the whole story is ONE test).
    private const string TestCountry = "ZZ";
    private static readonly CityImportRules Rules = CityImportRules.Brazil with { Country = TestCountry };

    private static List<string> SampleLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Data", "geonames-br-sample.txt"))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Replace("\tBR\t", $"\t{TestCountry}\t"))
            .ToList();

    private static string WithPopulation(string line, long population)
    {
        var parts = line.Split('\t');
        parts[14] = population.ToString();
        return string.Join('\t', parts);
    }

    private static CityImportPlan Plan(IEnumerable<string> lines, IReadOnlyCollection<ManualCity>? manual = null) =>
        CityImportPlanner.Plan(lines, Rules,
            [new CityNameOverride("2400208", "Assú")], manual);

    private Task<CityImportResult> ImportAsync(CityImportPlan plan) =>
        WithDbAsync(db => new CityImportService(db, NullLogger<CityImportService>.Instance).ImportAsync(plan, TestCountry));

    [Fact]
    public async Task ImportingTheSample_IsIdempotent_KeepsSlugsAndFollowsTheDataset()
    {
        var lines = SampleLines();

        // ---- first run: 8 municipalities (the seat "Imbituba" locality is dropped) + 3 localities
        var plan = Plan(lines);
        Assert.Equal(8, plan.Municipalities.Count);
        Assert.Equal(3, plan.Localities.Count);
        Assert.Equal(1, plan.Stats.LocalitiesSameNameAsMunicipality);

        var first = await ImportAsync(plan);
        Assert.Equal(new CityImportResult(11, 0, 0, 0, 0), first);

        var slugs = await WithDbAsync(db => db.Cities.Where(c => c.Country == TestCountry).Select(c => c.Slug).OrderBy(s => s).ToListAsync());
        Assert.Equal(
            [
                "assu-rn", "brasilia-df", "buritis-mg", "goiasminas-buritis-mg", "imbituba-sc", "marinas-santa-helena-pr",
                "praia-do-rosa-imbituba-sc", "santa-helena-ma", "santa-helena-pb", "santa-helena-pr", "santa-helena-sc",
            ],
            slugs);

        var cities = await WithDbAsync(db => db.Cities.AsNoTracking().Where(c => c.Country == TestCountry).ToListAsync());
        var assu = cities.Single(c => c.Slug == "assu-rn");
        Assert.Equal("Assú", assu.Name);          // IBGE spelling wins
        Assert.Equal("Açu", assu.SourceName);     // what the dataset said
        Assert.Equal("2400208", assu.NationalCode);

        var rosa = cities.Single(c => c.Slug == "praia-do-rosa-imbituba-sc");
        Assert.Equal(CityKind.Locality, rosa.Kind);
        Assert.Equal(cities.Single(c => c.Slug == "imbituba-sc").Id, rosa.ParentCityId);
        Assert.Equal(cities.Single(c => c.Slug == "santa-helena-pr").Id, cities.Single(c => c.Slug == "marinas-santa-helena-pr").ParentCityId);
        Assert.Contains("goiaminas", cities.Single(c => c.Slug == "goiasminas-buritis-mg").SearchText);
        Assert.Equal("DF", cities.Single(c => c.Slug == "brasilia-df").AdminArea);

        // ---- second run: nothing changes
        var second = await ImportAsync(Plan(lines));
        Assert.Equal(new CityImportResult(0, 0, 11, 0, 0), second);

        // ---- the dataset moves on: a new population, and one municipality vanishes
        var imbituba = lines.Single(l => l.StartsWith("6323145\t"));
        var santaHelenaSc = lines.Single(l => l.StartsWith("6323257\t"));
        var changed = lines.Where(l => l != santaHelenaSc).Select(l => l == imbituba ? WithPopulation(l, 99999) : l).ToList();

        var third = await ImportAsync(Plan(changed));
        Assert.Equal(new CityImportResult(0, 1, 9, 0, 1), third);

        var afterChange = await WithDbAsync(db => db.Cities.AsNoTracking().Where(c => c.Country == TestCountry).ToListAsync());
        Assert.Equal(99999, afterChange.Single(c => c.Slug == "imbituba-sc").Population);
        var vanished = afterChange.Single(c => c.Slug == "santa-helena-sc");
        Assert.False(vanished.IsActive);   // deactivated, never deleted

        // ---- it comes back: reactivated, same slug, same row
        var fourth = await ImportAsync(Plan(lines));
        Assert.Equal(new CityImportResult(0, 1, 9, 1, 0), fourth);   // Imbituba back to its old population, Santa Helena SC reactivated
        var back = await WithDbAsync(db => db.Cities.AsNoTracking().SingleAsync(c => c.Slug == "santa-helena-sc"));
        Assert.True(back.IsActive);
        Assert.Equal(vanished.Id, back.Id);

        // ---- a municipality added by hand is adopted (not duplicated) once the dataset carries it
        var manual = new ManualCity("5101837", "Boa Esperança do Norte", "MT", -13.5067, -55.1486, "America/Cuiaba", 5877);
        await ImportAsync(Plan(lines, [manual]));
        var handAdded = await WithDbAsync(db => db.Cities.AsNoTracking().SingleAsync(c => c.NationalCode == "5101837"));
        Assert.Equal("manual", handAdded.Source);
        Assert.Equal("boa-esperanca-do-norte-mt", handAdded.Slug);

        var datasetNowHasIt = GeoNamesParserTestsLine(900001, "Boa Esperança do Norte", "A", "ADM2", "14", "5101837");
        await ImportAsync(Plan(lines.Append(datasetNowHasIt), [manual]));

        var adopted = await WithDbAsync(db => db.Cities.AsNoTracking().Where(c => c.NationalCode == "5101837").ToListAsync());
        var only = Assert.Single(adopted);
        Assert.Equal(handAdded.Id, only.Id);
        Assert.Equal("geonames", only.Source);
        Assert.Equal(900001, only.ExternalId);
        Assert.Equal("boa-esperanca-do-norte-mt", only.Slug);   // the URL that may already be indexed does not change
    }

    private static string GeoNamesParserTestsLine(long id, string name, string cls, string code, string admin1, string admin2) =>
        string.Join('\t', id, name, name, "", "-13.5", "-55.1", cls, code, TestCountry, "", admin1, admin2, "", "", 0, "", "", "America/Cuiaba", "2024-01-01");
}
