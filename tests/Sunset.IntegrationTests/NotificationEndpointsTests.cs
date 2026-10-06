using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Notifications;
using Sunset.Application.DTOs.Photos;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class NotificationEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task LikingAnothersPhoto_NotifiesTheOwnerAndRaisesUnreadCount()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, likerClient) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(ownerClient);
        var photo = await CreatePhotoAsync(ownerClient, location.Id);

        var likeResponse = await likerClient.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);
        Assert.Equal(HttpStatusCode.NoContent, likeResponse.StatusCode);

        var unreadCount = await ownerClient.GetFromJsonAsync<NotificationUnreadCountResponse>("/api/v1/notifications/unread-count");
        Assert.Equal(1, unreadCount!.Count);

        var page = await ownerClient.GetFromJsonAsync<CursorPagedResult<NotificationResponse>>("/api/v1/notifications");
        Assert.Contains(page!.Items, n => n.Type == Sunset.Domain.Enums.NotificationType.PhotoLiked && n.TargetDescription == $"Photo:{photo.Id}");
    }

    [Fact]
    public async Task LikingOwnPhoto_DoesNotNotify()
    {
        var (_, ownerClient) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(ownerClient);
        var photo = await CreatePhotoAsync(ownerClient, location.Id);

        await ownerClient.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);

        var unreadCount = await ownerClient.GetFromJsonAsync<NotificationUnreadCountResponse>("/api/v1/notifications/unread-count");
        Assert.Equal(0, unreadCount!.Count);
    }

    [Fact]
    public async Task MarkRead_ReducesUnreadCount()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, likerClient) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(ownerClient);
        var photo = await CreatePhotoAsync(ownerClient, location.Id);
        await likerClient.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);

        var page = await ownerClient.GetFromJsonAsync<CursorPagedResult<NotificationResponse>>("/api/v1/notifications");
        var notificationId = page!.Items[0].Id;

        var markReadResponse = await ownerClient.PostAsync($"/api/v1/notifications/{notificationId}/read", null);

        Assert.Equal(HttpStatusCode.NoContent, markReadResponse.StatusCode);
        var unreadCount = await ownerClient.GetFromJsonAsync<NotificationUnreadCountResponse>("/api/v1/notifications/unread-count");
        Assert.Equal(0, unreadCount!.Count);
    }

    [Fact]
    public async Task MarkRead_WhenNotTheRecipient_ReturnsUnauthorized()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, likerClient) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(ownerClient);
        var photo = await CreatePhotoAsync(ownerClient, location.Id);
        await likerClient.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);

        var page = await ownerClient.GetFromJsonAsync<CursorPagedResult<NotificationResponse>>("/api/v1/notifications");
        var notificationId = page!.Items[0].Id;

        var response = await likerClient.PostAsync($"/api/v1/notifications/{notificationId}/read", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkAllRead_ClearsUnreadCount()
    {
        var (_, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, likerClient) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(ownerClient);
        var photoA = await CreatePhotoAsync(ownerClient, location.Id);
        var photoB = await CreatePhotoAsync(ownerClient, location.Id);
        await likerClient.PostAsync($"/api/v1/photos/{photoA.Id}/likes", null);
        await likerClient.PostAsync($"/api/v1/photos/{photoB.Id}/likes", null);

        var response = await ownerClient.PostAsync("/api/v1/notifications/read-all", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var unreadCount = await ownerClient.GetFromJsonAsync<NotificationUnreadCountResponse>("/api/v1/notifications/unread-count");
        Assert.Equal(0, unreadCount!.Count);
    }

    [Fact]
    public async Task GetAll_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/v1/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CommentingOnAnothersPhoto_NotifiesTheOwner()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync();
        var (_, commenterClient) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(ownerClient);
        var photo = await CreatePhotoAsync(ownerClient, location.Id);

        var commentResponse = await commenterClient.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/comments", new { content = "Muito lindo!" });
        commentResponse.EnsureSuccessStatusCode();

        var page = await ownerClient.GetFromJsonAsync<CursorPagedResult<NotificationResponse>>("/api/v1/notifications");
        Assert.Contains(page!.Items, n => n.Type == Sunset.Domain.Enums.NotificationType.PhotoCommented);
    }

    [Fact]
    public async Task ReplyingToAnothersComment_NotifiesTheParentAuthorOnly()
    {
        var (owner, ownerClient) = await RegisterAndAuthenticateAsync();
        var (parentAuthor, parentAuthorClient) = await RegisterAndAuthenticateAsync();
        var (_, replierClient) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(ownerClient);
        var photo = await CreatePhotoAsync(ownerClient, location.Id);

        var rootCommentResponse = await parentAuthorClient.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/comments", new { content = "Comentario raiz" });
        var rootComment = await rootCommentResponse.Content.ReadFromJsonAsync<CommentResponse>();

        var replyResponse = await replierClient.PostAsJsonAsync(
            $"/api/v1/photos/{photo.Id}/comments",
            new { content = "Resposta", parentCommentId = rootComment!.Id });
        replyResponse.EnsureSuccessStatusCode();

        // O owner é notificado pelo comentário raiz (PhotoCommented), mas a resposta a esse
        // comentário não deve gerar uma segunda notificação pra ele - só pro autor do comentário
        // raiz (CommentReplied).
        var ownerNotifications = await ownerClient.GetFromJsonAsync<CursorPagedResult<NotificationResponse>>("/api/v1/notifications");
        Assert.DoesNotContain(ownerNotifications!.Items, n => n.Type == Sunset.Domain.Enums.NotificationType.CommentReplied);

        var parentAuthorNotifications = await parentAuthorClient.GetFromJsonAsync<CursorPagedResult<NotificationResponse>>("/api/v1/notifications");
        Assert.Contains(parentAuthorNotifications!.Items, n => n.Type == Sunset.Domain.Enums.NotificationType.CommentReplied);
    }
}
