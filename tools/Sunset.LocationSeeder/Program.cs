using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sunset.Application.Cities;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Services;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence;
using Sunset.Infrastructure.Persistence.Repositories;

// Registers a curated list of locations (data/launch-locations.json) through the application's own
// LocationService - so slugs, the city link and the duplicate check behave exactly as for a user.
//
//   dotnet run --project tools/Sunset.LocationSeeder -- --created-by admin@example.com            (simulation: rolled back)
//   dotnet run --project tools/Sunset.LocationSeeder -- --created-by admin@example.com --apply     (writes)
//
// The connection string comes from --connection or ConnectionStrings__DefaultConnection.
// Re-running is safe: a place that already exists is reported as a duplicate and skipped.

var options = ParseArgs(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: Sunset.LocationSeeder --created-by <email of the account that will own them> [--file <json>] [--apply] [--connection <string>]");
    return 2;
}

var connection = options.Connection ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("No connection string (--connection or ConnectionStrings__DefaultConnection).");
    return 2;
}

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
var list = JsonSerializer.Deserialize<SeedFile>(File.ReadAllText(options.File), json) ?? throw new InvalidOperationException("Empty file.");

await using var db = new SunsetDbContext(new DbContextOptionsBuilder<SunsetDbContext>().UseNpgsql(connection).Options);
await db.Database.OpenConnectionAsync();
await using var tx = await db.Database.BeginTransactionAsync();

var owner = await new UserRepository(db).GetByEmailAsync(options.CreatedBy);
if (owner is null)
{
    Console.Error.WriteLine($"No account with e-mail {options.CreatedBy}.");
    return 2;
}

var locations = new LocationRepository(db);
var cities = new CityRepository(db);
var service = new LocationService(locations, new PhotoRepository(db), new NoSunset(), cities, new CityResolver(cities), new NoCache());

int created = 0, skipped = 0, failed = 0;
foreach (var item in list.Locations)
{
    try
    {
        var city = await cities.GetBySlugAsync(item.City) ?? throw new InvalidOperationException($"municipality '{item.City}' is not in the catalog");
        if (city.Kind != CityKind.Municipality)
            throw new InvalidOperationException($"'{item.City}' is not a municipality");
        var cityKm = GeoMath.DistanceKm(item.Latitude, item.Longitude, city.Latitude, city.Longitude);
        if (cityKm > 80)
            throw new InvalidOperationException($"the point is {cityKm:N0} km from the centre of {city.Name} (check the coordinates)");

        var targetId = city.Id;
        if (item.Locality is not null)
        {
            var locality = await cities.GetBySlugAsync(item.Locality) ?? throw new InvalidOperationException($"locality '{item.Locality}' is not in the catalog");
            if (locality.Kind != CityKind.Locality || locality.ParentCityId != city.Id)
                throw new InvalidOperationException($"'{item.Locality}' is not a locality of {city.Name}");
            var localityKm = GeoMath.DistanceKm(item.Latitude, item.Longitude, locality.Latitude, locality.Longitude);
            if (localityKm > 10)
                throw new InvalidOperationException($"the point is {localityKm:N1} km from {locality.Name} (check the coordinates)");
            targetId = locality.Id;
        }

        var response = await service.CreateAsync(owner.Id,
            new CreateLocationRequest(item.Name, item.Latitude, item.Longitude, CityId: targetId, Description: item.Description, Category: item.Category));
        created++;
        Console.WriteLine($"  + {item.Name,-42} {response.Slug}   ({cityKm:N1} km from {city.Name})");
    }
    catch (ConflictException ex)
    {
        skipped++;
        Console.WriteLine($"  = {item.Name,-42} already registered (similar: {string.Join(", ", ex.Errors?["candidateLocationIds"] ?? [])})");
    }
    catch (Exception ex) when (ex is InvalidOperationException or ValidationException or NotFoundException)
    {
        failed++;
        Console.WriteLine($"  ! {item.Name,-42} {ex.Message}");
    }
}

Console.WriteLine($"\n{created} to create, {skipped} already there, {failed} with a problem.");
if (failed > 0)
{
    await tx.RollbackAsync();
    Console.WriteLine("Problems found: nothing was written.");
    return 1;
}

if (!options.Apply)
{
    await tx.RollbackAsync();
    Console.WriteLine("Simulation only (rolled back). Add --apply to write.");
    return 0;
}

await tx.CommitAsync();
Console.WriteLine("Written.");
return 0;

static Options? ParseArgs(string[] args)
{
    string? file = null, connection = null, createdBy = null;
    var apply = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--file" when i + 1 < args.Length: file = args[++i]; break;
            case "--connection" when i + 1 < args.Length: connection = args[++i]; break;
            case "--created-by" when i + 1 < args.Length: createdBy = args[++i]; break;
            case "--apply": apply = true; break;
            default: return null;
        }
    }

    file ??= Path.Combine(AppContext.BaseDirectory, "data", "launch-locations.json");
    return createdBy is null ? null : new Options(file, connection, createdBy, apply);
}

internal sealed record Options(string File, string? Connection, string CreatedBy, bool Apply);

internal sealed class SeedFile
{
    public List<SeedLocation> Locations { get; init; } = [];
}

internal sealed class SeedLocation
{
    public string Name { get; init; } = "";
    public LocationCategory? Category { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string City { get; init; } = "";
    public string? Locality { get; init; }
    public string? Description { get; init; }
}

// The seeder never reads the sunset time or serves cached pages; these satisfy the service's constructor.
internal sealed class NoSunset : ISunsetTimeService
{
    public Task<Sunset.Application.DTOs.Locations.SunsetTimeResponse> GetSunsetTimeAsync(double latitude, double longitude, DateOnly? date, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class NoCache : IResponseCacheInvalidator
{
    public Task InvalidateAsync(ResponseCacheTag tags, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task InvalidateAsync(ResponseCacheTag tags, SiteChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
