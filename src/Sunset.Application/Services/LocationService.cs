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
    ISunsetTimeService sunsetTimeService) : ILocationService
{
    public async Task<CursorPagedResult<LocationResponse>> SearchAsync(LocationSearchQuery query, CancellationToken cancellationToken = default)
    {
        var page = await locationRepository.SearchAsync(query, cancellationToken);
        var items = page.Items.Select(l => l.ToResponse()).ToList();

        return new CursorPagedResult<LocationResponse>(items, page.NextCursor, page.HasMore);
    }

    public async Task<LocationResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var location = await locationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        return location.ToResponse();
    }

    public async Task<LocationResponse> CreateAsync(CreateLocationRequest request, CancellationToken cancellationToken = default)
    {
        var location = new Location(request.Name, request.Latitude, request.Longitude, request.City);
        await locationRepository.AddAsync(location, cancellationToken);

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
        return locations.Select(l => l.ToResponse()).ToList();
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
    }

    public async Task<SunsetTimeResponse> GetSunsetTimeAsync(Guid locationId, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var location = await locationRepository.GetByIdAsync(locationId, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        return await sunsetTimeService.GetSunsetTimeAsync(location.Latitude, location.Longitude, date, cancellationToken);
    }
}
