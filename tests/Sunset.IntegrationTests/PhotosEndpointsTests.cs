using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class PhotosEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    private static async Task<PhotoResponse> CreatePhotoAsync(HttpClient authenticatedClient, Guid locationId, string? caption = null)
    {
        var request = new CreatePhotoRequest(locationId, $"https://sunset-photos-test.s3.amazonaws.com/{Guid.NewGuid():N}.jpg", caption);
        var response = await authenticatedClient.PostAsJsonAsync("/api/v1/photos", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PhotoResponse>())!;
    }

    [Fact]
    public async Task CreateUploadUrl_ReturnsPutUrlAndFinalImageUrl()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PostAsJsonAsync("/api/v1/photos/upload-url", new CreatePhotoUploadUrlRequest("image/jpeg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PhotoUploadUrlResponse>();
        Assert.StartsWith("http://localhost:4566/sunset-photos-test/photos/", body!.UploadUrl);
        Assert.StartsWith("http://localhost:4566/sunset-photos-test/photos/", body.ImageUrl);
    }

    [Fact]
    public async Task CreateUploadUrl_WithUnsupportedContentType_ReturnsBadRequest()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PostAsJsonAsync("/api/v1/photos/upload-url", new CreatePhotoUploadUrlRequest("application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ThenGetById_ReturnsThePhoto()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(client);

        var photo = await CreatePhotoAsync(client, location.Id, "Que vista!");
        var getResponse = await client.GetAsync($"/api/v1/photos/{photo.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<PhotoResponse>();
        Assert.Equal("Que vista!", fetched!.Caption);
        Assert.Equal(0, fetched.LikesCount);
    }

    [Fact]
    public async Task Create_WithUnknownLocation_ReturnsNotFound()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/photos",
            new CreatePhotoRequest(Guid.NewGuid(), "https://example.com/photo.jpg", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByNonAuthor_IsRejected()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);

        var (_, otherUser) = await RegisterAndAuthenticateAsync();
        var response = await otherUser.DeleteAsync($"/api/v1/photos/{photo.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByAuthor_RemovesThePhoto()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(client);
        var photo = await CreatePhotoAsync(client, location.Id);

        var deleteResponse = await client.DeleteAsync($"/api/v1/photos/{photo.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/photos/{photo.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task LikeThenUnlike_TogglesLikesCount()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);

        var (_, liker) = await RegisterAndAuthenticateAsync();

        var likeResponse = await liker.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);
        Assert.Equal(HttpStatusCode.NoContent, likeResponse.StatusCode);

        var afterLike = await liker.GetFromJsonAsync<PhotoResponse>($"/api/v1/photos/{photo.Id}");
        Assert.Equal(1, afterLike!.LikesCount);
        Assert.True(afterLike.LikedByCurrentUser);

        var unlikeResponse = await liker.DeleteAsync($"/api/v1/photos/{photo.Id}/likes");
        Assert.Equal(HttpStatusCode.NoContent, unlikeResponse.StatusCode);

        var afterUnlike = await liker.GetFromJsonAsync<PhotoResponse>($"/api/v1/photos/{photo.Id}");
        Assert.Equal(0, afterUnlike!.LikesCount);
        Assert.False(afterUnlike.LikedByCurrentUser);
    }

    [Fact]
    public async Task Like_Twice_IsIdempotent()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);

        await owner.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);
        await owner.PostAsync($"/api/v1/photos/{photo.Id}/likes", null);

        var response = await owner.GetFromJsonAsync<PhotoResponse>($"/api/v1/photos/{photo.Id}");
        Assert.Equal(1, response!.LikesCount);
    }

    [Fact]
    public async Task AddComment_ThenListComments_ReturnsIt()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);

        var addResponse = await owner.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/comments", new CreateCommentRequest("Muito bonito!"));
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var comment = await addResponse.Content.ReadFromJsonAsync<CommentResponse>();

        var listResponse = await owner.GetAsync($"/api/v1/photos/{photo.Id}/comments");
        var page = await listResponse.Content.ReadFromJsonAsync<CursorPagedResult<CommentResponse>>();

        Assert.Contains(page!.Items, c => c.Id == comment!.Id && c.Content == "Muito bonito!");

        var photoAfter = await owner.GetFromJsonAsync<PhotoResponse>($"/api/v1/photos/{photo.Id}");
        Assert.Equal(1, photoAfter!.CommentsCount);
    }

    [Fact]
    public async Task GetCommentById_ReturnsItWithPhotoId()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var addResponse = await owner.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/comments", new CreateCommentRequest("Muito bonito!"));
        var comment = (await addResponse.Content.ReadFromJsonAsync<CommentResponse>())!;

        var response = await owner.GetFromJsonAsync<CommentResponse>($"/api/v1/comments/{comment.Id}");

        Assert.Equal("Muito bonito!", response!.Content);
        Assert.Equal(photo.Id, response.PhotoId);
    }

    [Fact]
    public async Task GetCommentById_WithUnknownId_ReturnsNotFound()
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/api/v1/comments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddReply_IncrementsParentRepliesCount()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var rootResponse = await owner.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/comments", new CreateCommentRequest("Comentario raiz"));
        var root = (await rootResponse.Content.ReadFromJsonAsync<CommentResponse>())!;

        await owner.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/comments", new CreateCommentRequest("Resposta", root.Id));

        var repliesResponse = await owner.GetAsync($"/api/v1/comments/{root.Id}/replies");
        var replies = await repliesResponse.Content.ReadFromJsonAsync<CursorPagedResult<CommentResponse>>();
        Assert.Single(replies!.Items);
        Assert.Equal("Resposta", replies.Items[0].Content);
    }

    [Fact]
    public async Task DeleteComment_ByNonAuthor_IsRejected()
    {
        var (_, owner) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(owner);
        var photo = await CreatePhotoAsync(owner, location.Id);
        var addResponse = await owner.PostAsJsonAsync($"/api/v1/photos/{photo.Id}/comments", new CreateCommentRequest("Comentario"));
        var comment = (await addResponse.Content.ReadFromJsonAsync<CommentResponse>())!;

        var (_, otherUser) = await RegisterAndAuthenticateAsync();
        var response = await otherUser.DeleteAsync($"/api/v1/comments/{comment.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetFeed_SortedByRecent_IncludesCreatedPhoto()
    {
        var (_, client) = await RegisterAndAuthenticateAsync();
        var location = await CreateLocationAsync(client);
        var photo = await CreatePhotoAsync(client, location.Id);

        var response = await client.GetAsync("/api/v1/photos?sort=recent&limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CursorPagedResult<PhotoResponse>>();
        Assert.Contains(page!.Items, p => p.Id == photo.Id);
    }
}
