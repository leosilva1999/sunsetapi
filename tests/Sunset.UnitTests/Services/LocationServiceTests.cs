using Moq;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class LocationServiceTests
{
    private readonly Mock<ILocationRepository> _locationRepository = new();
    private readonly Mock<IPhotoRepository> _photoRepository = new();
    private readonly Mock<ISunsetTimeService> _sunsetTimeService = new();
    private readonly LocationService _sut;

    public LocationServiceTests()
    {
        _sut = new LocationService(_locationRepository.Object, _photoRepository.Object, _sunsetTimeService.Object);
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
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var created = new Rating(user.Id, location.Id, 5, "Muito bom!");
        SetRatingUser(created, user);

        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _locationRepository
            .SetupSequence(r => r.GetRatingAsync(user.Id, location.Id, default))
            .ReturnsAsync((Rating?)null)
            .ReturnsAsync(created);
        _locationRepository.Setup(r => r.GetAverageRatingAsync(location.Id, default)).ReturnsAsync(4.5m);

        var response = await _sut.RateAsync(user.Id, location.Id, new CreateRatingRequest(5, "Muito bom!"));

        Assert.Equal(5, response.Score);
        Assert.Equal("Muito bom!", response.Comment);
        Assert.Equal(4.5m, location.AvgRating);
        _locationRepository.Verify(r => r.AddRatingAsync(It.Is<Rating>(rt => rt.Score == 5 && rt.UserId == user.Id), default), Times.Once);
        _locationRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RateAsync_WithExistingRating_UpdatesScoreInsteadOfCreating()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var existingRating = new Rating(user.Id, location.Id, 3);
        SetRatingUser(existingRating, user);

        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _locationRepository.Setup(r => r.GetRatingAsync(user.Id, location.Id, default)).ReturnsAsync(existingRating);
        _locationRepository.Setup(r => r.GetAverageRatingAsync(location.Id, default)).ReturnsAsync(5m);

        var response = await _sut.RateAsync(user.Id, location.Id, new CreateRatingRequest(5, "Melhor ainda!"));

        Assert.Equal(5, existingRating.Score);
        Assert.Equal(5, response.Score);
        Assert.Equal("Melhor ainda!", response.Comment);
        Assert.Equal(5m, location.AvgRating);
        _locationRepository.Verify(r => r.AddRatingAsync(It.IsAny<Rating>(), default), Times.Never);
        _locationRepository.Verify(r => r.SaveChangesAsync(default), Times.Exactly(2));
    }

    [Fact]
    public async Task GetMyRatingAsync_WithNoRating_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetRatingAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default)).ReturnsAsync((Rating?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetMyRatingAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteRatingAsync_RemovesRatingAndRecalculatesAverage()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var rating = new Rating(user.Id, location.Id, 5);

        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _locationRepository.Setup(r => r.GetRatingAsync(user.Id, location.Id, default)).ReturnsAsync(rating);
        _locationRepository.Setup(r => r.GetAverageRatingAsync(location.Id, default)).ReturnsAsync(0m);

        await _sut.DeleteRatingAsync(user.Id, location.Id);

        Assert.Equal(0m, location.AvgRating);
        _locationRepository.Verify(r => r.RemoveRatingAsync(rating, default), Times.Once);
    }

    [Fact]
    public async Task DeleteRatingAsync_WithNoRating_ThrowsNotFoundException()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _locationRepository.Setup(r => r.GetRatingAsync(It.IsAny<Guid>(), location.Id, default)).ReturnsAsync((Rating?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteRatingAsync(Guid.NewGuid(), location.Id));
        _locationRepository.Verify(r => r.RemoveRatingAsync(It.IsAny<Rating>(), default), Times.Never);
    }

    private static void SetRatingUser(Rating rating, User user) =>
        typeof(Rating).GetProperty(nameof(Rating.User))!.SetValue(rating, user);

    [Fact]
    public async Task RateAsync_WithUnknownLocation_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RateAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateRatingRequest(5)));
    }

    [Fact]
    public async Task GetSunsetTimeAsync_WithUnknownLocation_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetSunsetTimeAsync(Guid.NewGuid(), null));
        _sunsetTimeService.Verify(s => s.GetSunsetTimeAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateOnly?>(), default), Times.Never);
    }

    [Fact]
    public async Task GetSunsetTimeAsync_WithExistingLocation_DelegatesToSunsetTimeServiceWithCoordinates()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var expected = new SunsetTimeResponse(
            new DateOnly(2026, 9, 2), "America/Sao_Paulo", "-03:00",
            DateTimeOffset.Parse("2026-09-02T06:25:08-03:00"),
            DateTimeOffset.Parse("2026-09-02T18:02:31-03:00"),
            DateTimeOffset.Parse("2026-09-02T12:13:49-03:00"),
            41843);

        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _sunsetTimeService
            .Setup(s => s.GetSunsetTimeAsync(location.Latitude, location.Longitude, null, default))
            .ReturnsAsync(expected);

        var response = await _sut.GetSunsetTimeAsync(location.Id, null);

        Assert.Equal(expected, response);
    }
}
