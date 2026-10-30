using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class CityService(
    ICityRepository cityRepository,
    ILocationRepository locationRepository,
    IPhotoService photoService,
    ISunsetTimeService sunsetTimeService) : ICityService
{
    public async Task<CursorPagedResult<CityResponse>> SearchAsync(CityListQuery query, CancellationToken cancellationToken = default)
    {
        var page = await cityRepository.SearchAsync(query, cancellationToken);
        var items = page.Items.Select(c => c.ToResponse()).ToList();

        return new CursorPagedResult<CityResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<CityDetailResponse> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var (city, stats) = await RequirePublicCityAsync(slug, cancellationToken);
        var extras = await cityRepository.GetDetailExtrasAsync(city.Id, cancellationToken);

        return city.ToDetailResponse(stats, extras);
    }

    public async Task<CursorPagedResult<LocationResponse>> GetLocationsAsync(string slug, CityLocationSort sort, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var (city, _) = await RequirePublicCityAsync(slug, cancellationToken);

        var page = await locationRepository.GetByCityAsync(city.Id, sort, cursor, limit, cancellationToken);
        var items = await locationRepository.ToResponsesAsync(page.Items.Select(l => (l, (double?)null)).ToList(), cancellationToken);

        return new CursorPagedResult<LocationResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<CursorPagedResult<PhotoResponse>> GetPhotosAsync(string slug, PhotoSortOption sort, string? cursor, int limit, Guid? currentUserId, CancellationToken cancellationToken = default)
    {
        var (city, _) = await RequirePublicCityAsync(slug, cancellationToken);

        return await photoService.GetCityFeedAsync(city.Id, sort, cursor, limit, currentUserId, cancellationToken);
    }

    public async Task<SunsetTimeResponse> GetSunsetTimeAsync(string slug, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var (city, _) = await RequirePublicCityAsync(slug, cancellationToken);

        return await sunsetTimeService.GetSunsetTimeAsync(city.Latitude, city.Longitude, date, cancellationToken);
    }

    // A page exists only for an active municipality that has at least one visible location: an empty page
    // would be a thin, indexable URL. Localities have no public pages yet.
    private async Task<(City City, CityStats Stats)> RequirePublicCityAsync(string slug, CancellationToken cancellationToken)
    {
        var city = await cityRepository.GetBySlugAsync(slug, cancellationToken);
        if (city is null || city.Kind != CityKind.Municipality)
            throw new NotFoundException("City not found.");

        var stats = await cityRepository.GetStatsAsync(city.Id, cancellationToken);
        if (stats.LocationsCount == 0)
            throw new NotFoundException("City not found.");

        return (city, stats);
    }
}
