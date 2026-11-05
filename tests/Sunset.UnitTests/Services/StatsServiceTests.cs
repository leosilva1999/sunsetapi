using FluentValidation;
using Moq;
using Sunset.Application.DTOs.Stats;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;

namespace Sunset.UnitTests.Services;

public class StatsServiceTests
{
    private readonly Mock<IStatsRepository> _repository = new();
    private readonly StatsService _sut;

    public StatsServiceTests()
    {
        _repository.Setup(r => r.GetSummaryAsync(default)).ReturnsAsync(new GrowthSummary(9, 8, 1, 27, 2, 2));
        _repository.Setup(r => r.CountBeforeAsync(It.IsAny<GrowthMetric>(), It.IsAny<DateOnly>(), It.IsAny<string>(), default)).ReturnsAsync(0);
        _repository.Setup(r => r.GetNewPerPeriodAsync(It.IsAny<GrowthMetric>(), It.IsAny<GrowthGranularity>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), default))
            .ReturnsAsync(new Dictionary<DateOnly, int>());
        _sut = new StatsService(_repository.Object);
    }

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private void Given(GrowthMetric metric, int baseline, params (DateOnly Date, int Count)[] perPeriod)
    {
        _repository.Setup(r => r.CountBeforeAsync(metric, It.IsAny<DateOnly>(), It.IsAny<string>(), default)).ReturnsAsync(baseline);
        _repository.Setup(r => r.GetNewPerPeriodAsync(metric, It.IsAny<GrowthGranularity>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), default))
            .ReturnsAsync(perPeriod.ToDictionary(p => p.Date, p => p.Count));
    }

    [Fact]
    public async Task EveryDayOfTheRangeAppears_QuietOnesWithZero_AndTheTotalRunsOnFromTheBaseline()
    {
        Given(GrowthMetric.Users, baseline: 10, (D(10, 27), 4), (D(10, 29), 1));

        var result = await _sut.GetGrowthAsync(new GrowthQuery(D(10, 26), D(10, 30), GrowthGranularity.Day, "users"));

        var points = result.Series["users"];
        Assert.Equal([D(10, 26), D(10, 27), D(10, 28), D(10, 29), D(10, 30)], points.Select(p => p.Date));
        Assert.Equal([0, 4, 0, 1, 0], points.Select(p => p.New));
        Assert.Equal([10, 14, 14, 15, 15], points.Select(p => p.Total));   // starts from the 10 accounts that already existed
    }

    [Fact]
    public async Task Weeks_StartOnMonday_AndMonths_OnTheFirst()
    {
        // 2026-11-05 is a Thursday: its week starts on Monday 2026-11-02
        var weeks = await _sut.GetGrowthAsync(new GrowthQuery(D(10, 28), D(11, 5), GrowthGranularity.Week, "users"));
        var months = await _sut.GetGrowthAsync(new GrowthQuery(D(9, 15), D(11, 5), GrowthGranularity.Month, "users"));

        Assert.Equal([D(10, 26), D(11, 2)], weeks.Series["users"].Select(p => p.Date));
        Assert.Equal([D(9, 1), D(10, 1), D(11, 1)], months.Series["users"].Select(p => p.Date));
        _repository.Verify(r => r.GetNewPerPeriodAsync(GrowthMetric.Users, GrowthGranularity.Week, D(10, 26), D(11, 9), StatsService.TimeZoneId, default), Times.Once);
    }

    [Fact]
    public async Task WithoutAMetricList_AllFourComeBack_AndTheSummaryTravelsAlong()
    {
        var result = await _sut.GetGrowthAsync(new GrowthQuery(D(11, 1), D(11, 3), GrowthGranularity.Day, null));

        Assert.Equal(["users", "locations", "photos", "ratings"], result.Series.Keys);
        Assert.Equal(new GrowthSummary(9, 8, 1, 27, 2, 2), result.Summary);
        Assert.Equal("America/Sao_Paulo", result.TimeZone);
    }

    [Fact]
    public async Task Metrics_AreCaseInsensitive_Trimmed_AndDeduplicated()
    {
        var result = await _sut.GetGrowthAsync(new GrowthQuery(D(11, 1), D(11, 3), GrowthGranularity.Day, " Photos, users ,PHOTOS"));

        Assert.Equal(["photos", "users"], result.Series.Keys);
    }

    [Fact]
    public async Task WithoutDates_TheWindowEndsToday_ThirtyDaysTwelveWeeksOrTwelveMonths()
    {
        var days = await _sut.GetGrowthAsync(new GrowthQuery(null, null, GrowthGranularity.Day, "users"));
        var weeks = await _sut.GetGrowthAsync(new GrowthQuery(null, null, GrowthGranularity.Week, "users"));
        var months = await _sut.GetGrowthAsync(new GrowthQuery(null, null, GrowthGranularity.Month, "users"));

        Assert.Equal(30, days.Series["users"].Count);
        Assert.Equal(days.To, days.Series["users"][^1].Date);
        Assert.InRange(weeks.Series["users"].Count, 12, 13);
        Assert.Equal(12, months.Series["users"].Count);
    }

    [Fact]
    public async Task ABadRangeOrMetric_IsAValidationError_AndTheDatabaseIsNeverAsked()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetGrowthAsync(new GrowthQuery(D(11, 5), D(11, 1), GrowthGranularity.Day, null)));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetGrowthAsync(new GrowthQuery(new DateOnly(2024, 1, 1), D(11, 5), GrowthGranularity.Day, null)));   // > 400 days
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetGrowthAsync(new GrowthQuery(D(11, 1), D(11, 5), GrowthGranularity.Day, "users,revenue")));

        _repository.Verify(r => r.GetNewPerPeriodAsync(It.IsAny<GrowthMetric>(), It.IsAny<GrowthGranularity>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task ALongRange_IsFineWhenCoarser()
    {
        var result = await _sut.GetGrowthAsync(new GrowthQuery(new DateOnly(2024, 1, 1), D(11, 5), GrowthGranularity.Month, "users"));

        Assert.Equal(35, result.Series["users"].Count);
    }

    [Theory]
    [InlineData(2026, 11, 2, GrowthGranularity.Week, 2026, 11, 2)]    // a Monday stays
    [InlineData(2026, 11, 8, GrowthGranularity.Week, 2026, 11, 2)]    // a Sunday belongs to the week that started the Monday before
    [InlineData(2026, 12, 31, GrowthGranularity.Month, 2026, 12, 1)]
    [InlineData(2026, 3, 9, GrowthGranularity.Day, 2026, 3, 9)]
    public void PeriodStart_FindsTheFirstDay(int y, int m, int d, GrowthGranularity granularity, int ey, int em, int ed) =>
        Assert.Equal(new DateOnly(ey, em, ed), StatsService.PeriodStart(new DateOnly(y, m, d), granularity));
}
