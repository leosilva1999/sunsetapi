using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.DTOs.Users;
using Sunset.Domain.Enums;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class ModerationEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static async Task<PhotoResponse> CreatePhotoAsync(HttpClient authenticatedClient, Guid locationId)
    {
        var request = new CreatePhotoRequest(locationId, $"https://sunset-photos-test.s3.amazonaws.com/{Guid.NewGuid():N}.jpg", null);
        var response = await authenticatedClient.PostAsJsonAsync("/api/v1/photos", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PhotoResponse>())!;
    }

    [Fact]
    public async Task ReportPhoto_ThenDuplicateReport_ReturnsConflict()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var (_, reporter) = await RegisterAndAuthenticateAsync();

        var first = await reporter.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/reports", new CreateReportRequest(ReportReason.Spam));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var report = await first.Content.ReadFromJsonAsync<ReportResponse>();
        Assert.Equal(ReportStatus.Pending, report!.Status);

        var duplicate = await reporter.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/reports", new CreateReportRequest(ReportReason.Inappropriate));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task ReportComment_OnUnknownComment_ReturnsNotFound()
    {
        var (_, reporter) = await RegisterAndAuthenticateAsync();

        var response = await reporter.PostAsJsonAsync($"/api/v1/comments/{Guid.NewGuid()}/reports", new CreateReportRequest(ReportReason.Spam));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReports_ByRegularUser_ReturnsForbidden()
    {
        var (_, user) = await RegisterAndAuthenticateAsync();

        var response = await user.GetAsync("/api/v1/moderation/reports");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReports_ByModerator_ListsPendingReport()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var (_, reporter) = await RegisterAndAuthenticateAsync();
        var reportResponse = await reporter.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/reports", new CreateReportRequest(ReportReason.Harassment));
        var report = (await reportResponse.Content.ReadFromJsonAsync<ReportResponse>())!;

        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var listResponse = await moderator.GetAsync("/api/v1/moderation/reports?status=Pending&limit=50");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<CursorPagedResult<ReportResponse>>();
        Assert.Contains(page!.Items, r => r.Id == report.Id);
    }

    [Fact]
    public async Task ResolveReport_ByModerator_MarksResolved()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var (_, reporter) = await RegisterAndAuthenticateAsync();
        var reportResponse = await reporter.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/reports", new CreateReportRequest(ReportReason.Spam));
        var report = (await reportResponse.Content.ReadFromJsonAsync<ReportResponse>())!;

        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var resolveResponse = await moderator.PatchAsJsonAsync($"/api/v1/moderation/reports/{report.Id}", new ResolveReportRequest(ReportStatus.Resolved));

        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ReportResponse>();
        Assert.Equal(ReportStatus.Resolved, resolved!.Status);
        Assert.NotNull(resolved.ResolvedAt);
    }

    [Fact]
    public async Task ResolveReport_WithPendingStatus_ReturnsBadRequest()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var (_, reporter) = await RegisterAndAuthenticateAsync();
        var reportResponse = await reporter.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/reports", new CreateReportRequest(ReportReason.Spam));
        var report = (await reportResponse.Content.ReadFromJsonAsync<ReportResponse>())!;

        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var response = await moderator.PatchAsJsonAsync($"/api/v1/moderation/reports/{report.Id}", new ResolveReportRequest(ReportStatus.Pending));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletePhoto_ByModerator_SoftDeletesIt()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var deleteResponse = await moderator.DeleteAsync($"/api/v1/photos/{photo.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await owner.GetAsync($"/api/v1/photos/{photo.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeletePhoto_ByRegularUserOnOthersContent_StillReturnsUnauthorized()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var (_, otherUser) = await RegisterAndAuthenticateAsync();

        var response = await otherUser.DeleteAsync($"/api/v1/photos/{photo.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_ByModerator_SoftDeletesIt()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var addResponse = await owner.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/comments", new CreateCommentRequest("Comentario"));
        var comment = (await addResponse.Content.ReadFromJsonAsync<CommentResponse>())!;
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var deleteResponse = await moderator.DeleteAsync($"/api/v1/comments/{comment.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await owner.GetAsync($"/api/v1/photos/{photo.Id}/comments");
        var page = await listResponse.Content.ReadFromJsonAsync<CursorPagedResult<CommentResponse>>();
        Assert.DoesNotContain(page!.Items, c => c.Id == comment.Id);
    }

    [Fact]
    public async Task UpdateTermsByAdmin_ThenGetTerms_ReflectsTheNewVersion()
    {
        // Doesn't assume a prior version exists - the Testing environment never runs DbSeeder,
        // so a fresh test DB starts with none, unlike a real Development/production deploy.
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        var content = $"Termos de uso {Guid.NewGuid():N}";

        var updateResponse = await admin.PutAsJsonAsync("/api/v1/terms", new UpdateLegalDocumentRequest(content));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<LegalDocumentResponse>();
        Assert.Equal(content, updated!.Content);
        Assert.Equal(LegalDocumentType.TermsOfService, updated.DocumentType);

        var afterResponse = await CreateClient().GetAsync("/api/v1/terms");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        var after = await afterResponse.Content.ReadFromJsonAsync<LegalDocumentResponse>();
        Assert.Equal(updated.Version, after!.Version);
        Assert.Equal(content, after.Content);
    }

    [Fact]
    public async Task UpdateTerms_ByModeratorNotAdmin_ReturnsForbidden()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PutAsJsonAsync("/api/v1/terms", new UpdateLegalDocumentRequest("Tentativa de edicao"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePrivacyByAdmin_ThenGetPrivacy_ReflectsTheNewVersion()
    {
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        var content = $"Politica de privacidade {Guid.NewGuid():N}";

        var updateResponse = await admin.PutAsJsonAsync("/api/v1/privacy", new UpdateLegalDocumentRequest(content));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<LegalDocumentResponse>();
        Assert.Equal(content, updated!.Content);
        Assert.Equal(LegalDocumentType.PrivacyPolicy, updated.DocumentType);

        var afterResponse = await CreateClient().GetAsync("/api/v1/privacy");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        var after = await afterResponse.Content.ReadFromJsonAsync<LegalDocumentResponse>();
        Assert.Equal(updated.Version, after!.Version);
    }

    [Fact]
    public async Task UpdatePrivacy_ByModeratorNotAdmin_ReturnsForbidden()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.PutAsJsonAsync("/api/v1/privacy", new UpdateLegalDocumentRequest("Tentativa de edicao"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangeUserRole_ByAdmin_PromotesUser()
    {
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        var (targetAuth, _) = await RegisterAndAuthenticateAsync();

        var response = await admin.PatchAsJsonAsync($"/api/v1/moderation/users/{targetAuth.User.Id}/role", new ChangeUserRoleRequest(UserRole.Moderator));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(UserRole.Moderator, updated!.Role);
    }

    [Fact]
    public async Task ChangeUserRole_ByModeratorNotAdmin_ReturnsForbidden()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);
        var (targetAuth, _) = await RegisterAndAuthenticateAsync();

        var response = await moderator.PatchAsJsonAsync($"/api/v1/moderation/users/{targetAuth.User.Id}/role", new ChangeUserRoleRequest(UserRole.Moderator));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SearchUsers_ByAdmin_FindsUserByNameSubstring()
    {
        var (_, admin) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Admin);
        var uniqueName = $"Zzyzx Moderation Search {Guid.NewGuid():N}";
        var (targetAuth, _) = await RegisterAndAuthenticateAsync(name: uniqueName);

        var response = await admin.GetAsync($"/api/v1/moderation/users?q={Uri.EscapeDataString(uniqueName[..20])}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CursorPagedResult<UserResponse>>();
        Assert.Contains(page!.Items, u => u.Id == targetAuth.User.Id);
    }

    [Fact]
    public async Task SearchUsers_ByModeratorNotAdmin_ReturnsForbidden()
    {
        var (_, moderator) = await RegisterAndAuthenticateWithRoleAsync(UserRole.Moderator);

        var response = await moderator.GetAsync("/api/v1/moderation/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
