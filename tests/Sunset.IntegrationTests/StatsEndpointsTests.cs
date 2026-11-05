using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sunset.Application.DTOs.Stats;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class StatsEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Growth = "/api/v1/moderation/stats/growth";

    private static async Task<GrowthResponse> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GrowthResponse>())!;
    }

    [Fact]
    public async Task OnlyModerationCanSeeIt_AnonymousIs401_AndACommonUserIs403()
    {
        var (_, user) = await RegisterAndAuthenticateAsync();
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateClient().GetAsync(Growth)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(Growth)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync(Growth)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(Growth)).StatusCode);
    }

    [Fact]
    public async Task TheDefaultAnswer_HasThirtyDaysForAllFourMetrics_AndTheCurvesAgreeWithTheSummary()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var result = await ReadAsync(await moderator.GetAsync(Growth));

        Assert.Equal(GrowthGranularity.Day, result.Granularity);
        Assert.Equal(["users", "locations", "photos", "ratings"], result.Series.Keys);
        Assert.All(result.Series.Values, points => Assert.Equal(30, points.Count));
        Assert.True(result.Series["users"][^1].Total >= 1);                       // at least the moderator just created
        Assert.Equal(result.Summary.Users, result.Series["users"][^1].Total);     // the curve ends where the summary stands
        Assert.Equal(result.Summary.Locations, result.Series["locations"][^1].Total);
    }

    [Fact]
    public async Task ANewSignupAndANewLocation_AddToTodaysPoint_AndToTheRunningTotal()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var before = await ReadAsync(await moderator.GetAsync($"{Growth}?metrics=users,locations"));

        var (_, owner) = await RegisterAndAuthenticateAsync();
        await CreateLocationAsync(owner);
        var after = await ReadAsync(await moderator.GetAsync($"{Growth}?metrics=users,locations"));

        foreach (var metric in new[] { "users", "locations" })
        {
            var b = before.Series[metric][^1];
            var a = after.Series[metric][^1];
            Assert.Equal(b.Date, a.Date);
            Assert.True(a.New > b.New, $"{metric}: today's new count should have grown");
            Assert.True(a.Total > b.Total, $"{metric}: the running total should have grown");
        }
    }

    [Fact]
    public async Task AHiddenLocation_NoLongerCountsInTheCurve()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var withIt = await ReadAsync(await moderator.GetAsync($"{Growth}?metrics=locations"));

        (await moderator.DeleteAsync($"/api/v1/moderation/locations/{location.Id}")).EnsureSuccessStatusCode();
        var without = await ReadAsync(await moderator.GetAsync($"{Growth}?metrics=locations"));

        Assert.Equal(withIt.Series["locations"][^1].Total - 1, without.Series["locations"][^1].Total);
        Assert.Equal(withIt.Summary.Locations - 1, without.Summary.Locations);
    }

    [Fact]
    public async Task WeeksAndMonths_AlignToTheirFirstDay_AndTheDatesAreRespected()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var weeks = await ReadAsync(await moderator.GetAsync($"{Growth}?granularity=week&metrics=users&from=2026-10-01&to=2026-10-31"));
        var months = await ReadAsync(await moderator.GetAsync($"{Growth}?granularity=month&metrics=users&from=2026-01-15&to=2026-03-02"));

        Assert.All(weeks.Series["users"], p => Assert.Equal(DayOfWeek.Monday, p.Date.DayOfWeek));
        Assert.Equal(new DateOnly(2026, 9, 28), weeks.Series["users"][0].Date);
        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)], months.Series["users"].Select(p => p.Date));
        Assert.Equal(new DateOnly(2026, 10, 1), weeks.From);
        Assert.Equal(new DateOnly(2026, 10, 31), weeks.To);
    }

    [Fact]
    public async Task TheJsonReadsLikeAChartWantsIt_LowerCaseKeysAndCamelCaseFields()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        using var doc = JsonDocument.Parse(await (await moderator.GetAsync($"{Growth}?metrics=users&from=2026-11-01&to=2026-11-02")).Content.ReadAsStringAsync());

        var root = doc.RootElement;
        Assert.Equal("Day", root.GetProperty("granularity").GetString());
        Assert.Equal("America/Sao_Paulo", root.GetProperty("timeZone").GetString());
        var first = root.GetProperty("series").GetProperty("users")[0];
        Assert.Equal("2026-11-01", first.GetProperty("date").GetString());
        Assert.True(first.TryGetProperty("new", out _) && first.TryGetProperty("total", out _));
        Assert.True(root.GetProperty("summary").TryGetProperty("activeAccounts", out _));
    }

    [Theory]
    [InlineData("from=2026-11-05&to=2026-11-01")]          // backwards
    [InlineData("from=2020-01-01&to=2026-11-01")]          // more than 400 days
    [InlineData("metrics=users,revenue")]                  // unknown metric
    [InlineData("granularity=year")]                       // unknown granularity
    [InlineData("from=not-a-date")]
    public async Task BadParameters_Are400(string query)
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        Assert.Equal(HttpStatusCode.BadRequest, (await moderator.GetAsync($"{Growth}?{query}")).StatusCode);
    }
}
