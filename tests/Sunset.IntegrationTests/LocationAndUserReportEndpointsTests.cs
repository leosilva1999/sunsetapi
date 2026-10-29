using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Moderation;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class LocationAndUserReportEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ReportLocation_ThenDuplicate_ReturnsConflict_AndModeratorSeesIt()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var (_, reporter) = await RegisterAndAuthenticateAsync();

        var first = await reporter.PostAsJsonAsync($"/api/v1/locations/{location.Id}/reports", new CreateReportRequest(ReportReason.IncorrectInfo, "Pino no lugar errado"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var report = (await first.Content.ReadFromJsonAsync<ReportResponse>())!;
        Assert.Equal(ReportTargetType.Location, report.TargetType);
        Assert.Equal(location.Id, report.TargetId);

        var duplicate = await reporter.PostAsJsonAsync($"/api/v1/locations/{location.Id}/reports", new CreateReportRequest(ReportReason.Spam));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var list = await moderator.GetFromJsonAsync<CursorPagedResult<ReportResponse>>("/api/v1/moderation/reports?status=Pending&limit=50");
        Assert.Contains(list!.Items, r => r.Id == report.Id);
    }

    [Fact]
    public async Task ReportLocation_Unknown_ReturnsNotFound()
    {
        var (_, reporter) = await RegisterAndAuthenticateAsync();

        var response = await reporter.PostAsJsonAsync($"/api/v1/locations/{Guid.NewGuid()}/reports", new CreateReportRequest(ReportReason.Spam));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReportUser_Works_ButNotYourself()
    {
        var (target, _) = await RegisterAndAuthenticateAsync();
        var (reporterAuth, reporter) = await RegisterAndAuthenticateAsync();

        var ok = await reporter.PostAsJsonAsync($"/api/v1/users/{target.User.Id}/reports", new CreateReportRequest(ReportReason.Harassment));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var self = await reporter.PostAsJsonAsync($"/api/v1/users/{reporterAuth.User.Id}/reports", new CreateReportRequest(ReportReason.Spam));
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
    }

    [Fact]
    public async Task Report_WithoutLogin_ReturnsUnauthorized()
    {
        var response = await CreateClient().PostAsJsonAsync($"/api/v1/users/{Guid.NewGuid()}/reports", new CreateReportRequest(ReportReason.Spam));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
