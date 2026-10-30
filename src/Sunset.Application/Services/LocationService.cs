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
        var location = new Location(request.Name, request.Latitude, request.Longitude, cityText, userId);
        if (municipality is not null)
            location.ChangeCity(municipality, locality);

        await locationRepository.AddAsync(location, cancellationToken);
        await cacheInvalidator.InvalidateAsync(ResponseCacheTag.Locations | ResponseCacheTag.Cities, cancellationToken);

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
