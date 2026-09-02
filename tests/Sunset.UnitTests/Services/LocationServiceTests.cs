using Moq;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class LocationServiceTests
{
    private readonly Mock<ILocationRepository> _locationRepository = new();
    private readonly Mock<IPhotoRepository> _photoRepository = new();
    private readonly LocationService _sut;

    public LocationServiceTests()
    {
        _sut = new LocationService(_locationRepository.Object, _photoRepository.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownLocation_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_CreatesAndReturnsLocation()
    {
        var request = new CreateLocationRequest("Praia do Rosa", -28.13, -48.62, "Imbituba");

        var response = await _sut.CreateAsync(request);

        Assert.Equal("Praia do Rosa", response.Name);
        Assert.Equal("Imbituba", response.City);
        _locationRepository.Verify(r => r.AddAsync(It.Is<Location>(l => l.Name == "Praia do Rosa"), default), Times.Once);
    }

    [Fact]
    public async Task GetPhotosAsync_WithUnknownLocation_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetPhotosAsync(Guid.NewGuid(), null, 20));
        _photoRepository.Verify(r => r.GetByLocationIdAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task GetRankingAsync_ReturnsMappedLocations()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        _locationRepository
            .Setup(r => r.GetRankingAsync(RankingPeriod.Week, 10, default))
            .ReturnsAsync([location]);

        var ranking = await _sut.GetRankingAsync(RankingPeriod.Week, 10);

        Assert.Single(ranking);
        Assert.Equal(location.Id, ranking[0].Id);
    }

    [Fact]
    public async Task RateAsync_WithNoExistingRating_CreatesRatingAndRecalculatesAverage()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var userId = Guid.NewGuid();

        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _locationRepository.Setup(r => r.GetRatingAsync(userId, location.Id, default)).ReturnsAsync((Rating?)null);
        _locationRepository.Setup(r => r.GetAverageRatingAsync(location.Id, default)).ReturnsAsync(4.5m);

        var response = await _sut.RateAsync(userId, location.Id, new CreateRatingRequest(5));

        Assert.Equal(4.5m, response.AvgRating);
        _locationRepository.Verify(r => r.AddRatingAsync(It.Is<Rating>(rt => rt.Score == 5 && rt.UserId == userId), default), Times.Once);
        _locationRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RateAsync_WithExistingRating_UpdatesScoreInsteadOfCreating()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var userId = Guid.NewGuid();
        var existingRating = new Rating(userId, location.Id, 3);

        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _locationRepository.Setup(r => r.GetRatingAsync(userId, location.Id, default)).ReturnsAsync(existingRating);
        _locationRepository.Setup(r => r.GetAverageRatingAsync(location.Id, default)).ReturnsAsync(5m);

        var response = await _sut.RateAsync(userId, location.Id, new CreateRatingRequest(5));

        Assert.Equal(5, existingRating.Score);
        Assert.Equal(5m, response.AvgRating);
        _locationRepository.Verify(r => r.AddRatingAsync(It.IsAny<Rating>(), default), Times.Never);
        _locationRepository.Verify(r => r.SaveChangesAsync(default), Times.Exactly(2));
    }

    [Fact]
    public async Task RateAsync_WithUnknownLocation_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RateAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateRatingRequest(5)));
    }
}
