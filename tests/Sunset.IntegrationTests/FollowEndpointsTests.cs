using System.Net;
using System.Net.Http.Json;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Users;
using Sunset.IntegrationTests.Infrastructure;

namespace Sunset.IntegrationTests;

public class FollowEndpointsTests(SunsetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Follow_ThenGetById_ShowsFollowedTrueAndIncrementsCounters()
    {
        var (follower, followerClient) = await RegisterAndAuthenticateAsync();
        var (followee, _) = await RegisterAndAuthenticateAsync();

        var response = await followerClient.PostAsync($"/api/v1/users/{followee.User.Id}/follow", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var followeeProfile = await followerClient.GetFromJsonAsync<PublicUserResponse>($"/api/v1/users/{followee.User.Id}");
        Assert.True(followeeProfile!.IsFollowedByCurrentUser);
        Assert.Equal(1, followeeProfile.FollowersCount);

        var followerProfile = await followerClient.GetFromJsonAsync<PublicUserResponse>($"/api/v1/users/{follower.User.Id}");
        Assert.Equal(1, followerProfile!.FollowingCount);
    }

    [Fact]
    public async Task Follow_Self_ReturnsConflict()
    {
        var (auth, client) = await RegisterAndAuthenticateAsync();

        var response = await client.PostAsync($"/api/v1/users/{auth.User.Id}/follow", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Follow_WithoutAuth_ReturnsUnauthorized()
    {
        var (followee, _) = await RegisterAndAuthenticateAsync();
        var client = CreateClient();

        var response = await client.PostAsync($"/api/v1/users/{followee.User.Id}/follow", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Follow_Twice_IsIdempotent()
    {
        var (_, followerClient) = await RegisterAndAuthenticateAsync();
        var (followee, _) = await RegisterAndAuthenticateAsync();

        await followerClient.PostAsync($"/api/v1/users/{followee.User.Id}/follow", null);
        await followerClient.PostAsync($"/api/v1/users/{followee.User.Id}/follow", null);

        var followeeProfile = await followerClient.GetFromJsonAsync<PublicUserResponse>($"/api/v1/users/{followee.User.Id}");
        Assert.Equal(1, followeeProfile!.FollowersCount);
    }

    [Fact]
    public async Task Unfollow_WhenFollowing_DecrementsCountersAndClearsFlag()
    {
        var (_, followerClient) = await RegisterAndAuthenticateAsync();
        var (followee, _) = await RegisterAndAuthenticateAsync();
        await followerClient.PostAsync($"/api/v1/users/{followee.User.Id}/follow", null);

        var response = await followerClient.DeleteAsync($"/api/v1/users/{followee.User.Id}/follow");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var followeeProfile = await followerClient.GetFromJsonAsync<PublicUserResponse>($"/api/v1/users/{followee.User.Id}");
        Assert.False(followeeProfile!.IsFollowedByCurrentUser);
        Assert.Equal(0, followeeProfile.FollowersCount);
    }

    [Fact]
    public async Task Unfollow_WhenNotFollowing_IsIdempotent()
    {
        var (_, followerClient) = await RegisterAndAuthenticateAsync();
        var (followee, _) = await RegisterAndAuthenticateAsync();

        var response = await followerClient.DeleteAsync($"/api/v1/users/{followee.User.Id}/follow");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetFollowers_ReturnsUsersFollowingTheTarget()
    {
        var (follower, followerClient) = await RegisterAndAuthenticateAsync();
        var (followee, followeeClient) = await RegisterAndAuthenticateAsync();
        await followerClient.PostAsync($"/api/v1/users/{followee.User.Id}/follow", null);

        var response = await followeeClient.GetAsync($"/api/v1/users/{followee.User.Id}/followers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CursorPagedResult<PublicUserResponse>>();
        Assert.Contains(page!.Items, u => u.Id == follower.User.Id);
    }

    [Fact]
    public async Task GetFollowing_ReturnsUsersTheSourceFollows()
    {
        var (follower, followerClient) = await RegisterAndAuthenticateAsync();
        var (followee, _) = await RegisterAndAuthenticateAsync();
        await followerClient.PostAsync($"/api/v1/users/{followee.User.Id}/follow", null);

        var response = await followerClient.GetAsync($"/api/v1/users/{follower.User.Id}/following");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CursorPagedResult<PublicUserResponse>>();
        Assert.Contains(page!.Items, u => u.Id == followee.User.Id);
    }
}
