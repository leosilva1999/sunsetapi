using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sunset.Application.Cities.Import;
using Sunset.Infrastructure.Cities;
using Sunset.Infrastructure.Persistence;

// Loads cities from a GeoNames country dump. Not part of the API: it is run by hand (or by a job).
//
//   dotnet run --project tools/Sunset.CityImporter -- --country BR --file BR.zip --dry-run
//   dotnet run --project tools/Sunset.CityImporter -- --country BR --file BR.zip --connection "<postgres connection string>"
//
// --connection can be replaced by the ConnectionStrings__DefaultConnection environment variable.
// GeoNames data: CC BY 4.0, https://www.geonames.org/ (credit the source wherever the data is shown).

var options = ParseArgs(args);
if (options is null)
{
    Console.Error.WriteLine("""
        Usage: Sunset.CityImporter --country BR --file <BR.zip|BR.txt> [--dry-run] [--connection <string>]
                                   [--overrides <json>] [--additions <json>]
        """);
    return 2;
}

var rules = CityImportRules.For(options.Country);
if (rules is null)
{
    Console.Error.WriteLine($"No import rules for country '{options.Country}' yet (only BR).");
    return 2;
}

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var overrides = LoadJson<List<CityNameOverride>>(options.OverridesPath) ?? [];
var additions = LoadJson<List<ManualCity>>(options.AdditionsPath) ?? [];

Console.WriteLine($"Reading {options.File} ...");
var plan = CityImportPlanner.Plan(ReadLines(options.File), rules, overrides, additions);

var s = plan.Stats;
Console.WriteLine($"""
    Plan for {rules.Country}:
      municipalities ................... {s.Municipalities:N0} (of which added by hand: {s.ManualAdditions})
      localities ....................... {s.Localities:N0}
      localities with no municipality .. {s.LocalitiesWithoutMunicipality:N0} (skipped)
      localities named like their seat . {s.LocalitiesSameNameAsMunicipality:N0} (skipped: the municipality covers them)
      municipal name corrections ....... {s.NameOverridesApplied}
      malformed lines .................. {s.MalformedLines}
    """);

if (options.DryRun)
{
    Console.WriteLine("Dry run: nothing was written.");
    return 0;
}

var connection = options.Connection ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("Give --connection or set ConnectionStrings__DefaultConnection.");
    return 2;
}

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
await using var db = new SunsetDbContext(new DbContextOptionsBuilder<SunsetDbContext>().UseNpgsql(connection).Options);

var started = DateTime.UtcNow;
var result = await new CityImportService(db, loggerFactory.CreateLogger<CityImportService>()).ImportAsync(plan, rules.Country);

Console.WriteLine($"Done in {(DateTime.UtcNow - started).TotalSeconds:N0}s: {result.Inserted:N0} inserted, {result.Updated:N0} updated, " +
                  $"{result.Unchanged:N0} unchanged, {result.Reactivated:N0} reactivated, {result.Deactivated:N0} deactivated.");
return 0;

T? LoadJson<T>(string? path) where T : class =>
    path is not null && File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), jsonOptions) : null;

static IEnumerable<string> ReadLines(string file)
{
    if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var line in File.ReadLines(file))
            yield return line;
        yield break;
    }

    using var zip = ZipFile.OpenRead(file);
    var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                                                && !e.Name.Equals("readme.txt", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("No data file (.txt) inside the zip.");
    using var reader = new StreamReader(entry.Open());
    while (reader.ReadLine() is { } line)
        yield return line;
}

static ImporterOptions? ParseArgs(string[] args)
{
    string? country = null, file = null, connection = null, overrides = null, additions = null;
    var dryRun = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--country" when i + 1 < args.Length: country = args[++i]; break;
            case "--file" when i + 1 < args.Length: file = args[++i]; break;
            case "--connection" when i + 1 < args.Length: connection = args[++i]; break;
            case "--overrides" when i + 1 < args.Length: overrides = args[++i]; break;
            case "--additions" when i + 1 < args.Length: additions = args[++i]; break;
            case "--dry-run": dryRun = true; break;
            default: return null;
        }
    }

    if (country is null || file is null || !File.Exists(file))
        return null;

    var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
    return new ImporterOptions(country, file, dryRun, connection,
        overrides ?? Path.Combine(dataDir, "city-name-overrides.json"),
        additions ?? Path.Combine(dataDir, "city-additions.json"));
}

internal sealed record ImporterOptions(string Country, string File, bool DryRun, string? Connection, string? OverridesPath, string? AdditionsPath);
