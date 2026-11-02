using FluentValidation;
using FluentValidation.Results;
using Sunset.Application.Cities;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class LocationService(
    ILocationRepository locationRepository,
    IPhotoRepository photoRepository,
    ISunsetTimeService sunsetTimeService,
    ICityRepository cityRepository,
    CityResolver cityResolver,
    IResponseCacheInvalidator cacheInvalidator) : ILocationService
{
    public async Task<CursorPagedResult<LocationResponse>> SearchAsync(LocationSearchQuery query, CancellationToken cancellationToken = default)
    {
        var page = await locationRepository.SearchAsync(query, cancellationToken);
        var items = await locationRepository.ToResponsesAsync(page.Items.Select(l => (l.Location, l.DistanceKm)).ToList(), cancellationToken);

        return new CursorPagedResult<LocationResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<LocationResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var location = await locationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        return await locationRepository.ToResponseAsync(location, cancellationToken: cancellationToken);
    }

    public async Task<LocationResponse> GetByIdOrSlugAsync(string idOrSlug, CancellationToken cancellationToken = default) =>
        await locationRepository.ToResponseAsync(await RequireByIdOrSlugAsync(idOrSlug, cancellationToken), cancellationToken: cancellationToken);

    private async Task<Location> RequireByIdOrSlugAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var key = idOrSlug.Trim();
        var location = Guid.TryParse(key, out var id)
            ? await locationRepository.GetByIdAsync(id, cancellationToken)
            : SlugGenerator.IsWellFormed(key)
                ? await locationRepository.GetBySlugAsync(key.ToLowerInvariant(), cancellationToken)
                : null;

        return location ?? throw new NotFoundException("Location not found.");
    }

    public const int MaxNearbyLimit = 12;
    public const double DefaultNearbyRadiusKm = 30;

    public async Task<IReadOnlyList<LocationResponse>> GetNearbyAsync(string idOrSlug, double radiusKm, int limit, CancellationToken cancellationToken = default)
    {
        if (double.IsNaN(radiusKm) || radiusKm <= 0 || radiusKm > LocationSearchQuery.MaxRadiusKm)
            throw new ValidationException([new ValidationFailure("radiusKm", $"'radiusKm' must be greater than 0 and at most {LocationSearchQuery.MaxRadiusKm}.")]);

        var location = await RequireByIdOrSlugAsync(idOrSlug, cancellationToken);
        var size = Math.Clamp(limit, 1, MaxNearbyLimit);

        // The same "near me" search as GET /locations?lat=&lng=; one extra row because the location finds itself (distance 0).
        var page = await locationRepository.SearchAsync(
            new LocationSearchQuery(null, location.Latitude, location.Longitude, radiusKm, null, size + 1), cancellationToken);

        var others = page.Items.Where(i => i.Location.Id != location.Id).Take(size).Select(i => (i.Location, i.DistanceKm)).ToList();
        return await locationRepository.ToResponsesAsync(others, cancellationToken);
    }

    public async Task<LocationResponse> CreateAsync(Guid userId, CreateLocationRequest request, CancellationToken cancellationToken = default)
    {
        City? municipality = null;
        City? locality = null;

        if (request.CityId is { } cityId)
        {
            // A chosen catalog city wins over any free text sent along with it.
            var city = await cityRepository.GetActiveByIdAsync(cityId, cancellationToken);
            if (city is null || (city.Kind == CityKind.Locality && city.ParentCity is null))
                throw new ValidationException([new ValidationFailure(nameof(request.CityId), "City not found.")]);

            (municipality, locality) = city.Kind == CityKind.Locality ? (city.ParentCity, city) : (city, null);
        }
        else
        {
            // Legacy clients send only the text: link it when there is no doubt, otherwise keep the text
            // and let the review queue sort it out. A locality is never linked without a person confirming.
            var resolution = await cityResolver.ResolveAsync(request.City, request.Latitude, request.Longitude, cancellationToken);
            if (resolution.Kind == CityResolutionKind.Linked)
                municipality = resolution.Municipality;
        }

        var cityText = municipality is not null ? Location.CanonicalCityText(municipality) : request.City!.Trim();
        var location = new Location(request.Name, request.Latitude, request.Longitude, cityText, userId, request.Description, request.Category);
        if (municipality is not null)
            location.ChangeCity(municipality, locality);

        // "{name}-{city slug}" (just the name while no city is tied), with -2, -3... on a clash. Fixed for good.
        var baseSlug = SlugGenerator.ForLocation(request.Name, municipality?.Slug);
        var slug = baseSlug;
        for (var n = 2; await locationRepository.SlugExistsAsync(slug, cancellationToken); n++)
            slug = SlugGenerator.WithSuffix(baseSlug, n, SlugGenerator.LocationMaxLength);
        location.AssignSlug(slug);

        await locationRepository.AddAsync(location, cancellationToken);
        await cacheInvalidator.InvalidateAsync(ResponseCacheTag.Locations | ResponseCacheTag.Cities,
            SitePages.ForLocation(location, indexable: true), cancellationToken);

        return location.ToResponse();
    }

    public async Task<CursorPagedResult<PhotoResponse>> GetPhotosAsync(Guid locationId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        if (await locationRepository.GetByIdAsync(locationId, cancellationToken) is null)
            throw new NotFoundException("Location not found.");

        var page = await photoRepository.GetByLocationIdAsync(locationId, cursor, limit, cancellationToken);
        var items = page.Items.Select(p => p.ToResponse()).ToList();

        return new CursorPagedResult<PhotoResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<IReadOnlyList<LocationResponse>> GetRankingAsync(RankingPeriod period, int limit, CancellationToken cancellationToken = default)
    {
        var locations = await locationRepository.GetRankingAsync(period, limit, cancellationToken);
        return await locationRepository.ToResponsesAsync(locations.Select(l => (l, (double?)null)).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<LocationResponse>> GetRankingAsync(RankingPeriod period, int limit, string? citySlug, string? state, CancellationToken cancellationToken = default)
    {
        var filter = await BuildRankingFilterAsync(citySlug, state, cancellationToken);
        if (filter.IsEmpty)
            return await GetRankingAsync(period, limit, cancellationToken);

        var locations = await locationRepository.GetRankingAsync(period, limit, filter, cancellationToken);
        return await locationRepository.ToResponsesAsync(locations.Select(l => (l, (double?)null)).ToList(), cancellationToken);
    }

    private async Task<RankingFilter> BuildRankingFilterAsync(string? citySlug, string? state, CancellationToken cancellationToken)
    {
        string? upperState = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            upperState = state.Trim().ToUpperInvariant();
            if (upperState.Length > 10 || !upperState.All(char.IsLetter))
                throw new ValidationException([new ValidationFailure("state", "'state' must be a state code such as SC.")]);
        }

        Guid? cityId = null, localityId = null;
        if (!string.IsNullOrWhiteSpace(citySlug))
        {
            var city = await cityRepository.GetBySlugAsync(citySlug.Trim().ToLowerInvariant(), cancellationToken)
                ?? throw new NotFoundException("City not found.");
            if (city.Kind == CityKind.Locality)
                localityId = city.Id;
            else
                cityId = city.Id;
        }

        return new RankingFilter(cityId, localityId, upperState);
    }

    public async Task<RatingResponse> RateAsync(Guid userId, Guid locationId, CreateRatingRequest request, CancellationToken cancellationToken = default)
    {
        var location = await locationRepository.GetByIdAsync(locationId, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        var existingRating = await locationRepository.GetRatingAsync(userId, locationId, cancellationToken);
        if (existingRating is null)
        {
            var rating = new Rating(userId, locationId, request.Score, request.Comment);
            await locationRepository.AddRatingAsync(rating, cancellationToken);
        }
        else
        {
            existingRating.Update(request.Score, request.Comment);
            await locationRepository.SaveChangesAsync(cancellationToken);
        }

        var averageRating = await locationRepository.GetAverageRatingAsync(locationId, cancellationToken);
        location.RecalculateAvgRating(averageRating);
        await locationRepository.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateAsync(ResponseCacheTag.Locations | ResponseCacheTag.Cities, cancellationToken);

        var created = await locationRepository.GetRatingAsync(userId, locationId, cancellationToken)
            ?? throw new NotFoundException("Rating not found.");

        return created.ToResponse();
    }

    public async Task<CursorPagedResult<RatingResponse>> GetRatingsAsync(Guid locationId, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        if (await locationRepository.GetByIdAsync(locationId, cancellationToken) is null)
            throw new NotFoundException("Location not found.");

        var page = await locationRepository.GetRatingsAsync(locationId, cursor, limit, cancellationToken);
        var items = page.Items.Select(r => r.ToResponse()).ToList();

        return new CursorPagedResult<RatingResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<RatingResponse> GetMyRatingAsync(Guid userId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var rating = await locationRepository.GetRatingAsync(userId, locationId, cancellationToken)
            ?? throw new NotFoundException("Rating not found.");

        return rating.ToResponse();
    }

    public async Task DeleteRatingAsync(Guid userId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var location = await locationRepository.GetByIdAsync(locationId, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        var rating = await locationRepository.GetRatingAsync(userId, locationId, cancellationToken)
            ?? throw new NotFoundException("Rating not found.");

        await locationRepository.RemoveRatingAsync(rating, cancellationToken);

        var averageRating = await locationRepository.GetAverageRatingAsync(locationId, cancellationToken);
        location.RecalculateAvgRating(averageRating);
        await locationRepository.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateAsync(ResponseCacheTag.Locations | ResponseCacheTag.Cities, cancellationToken);
    }

    public async Task<SunsetTimeResponse> GetSunsetTimeAsync(Guid locationId, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var location = await locationRepository.GetByIdAsync(locationId, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        return await sunsetTimeService.GetSunsetTimeAsync(location.Latitude, location.Longitude, date, cancellationToken);
    }
}
