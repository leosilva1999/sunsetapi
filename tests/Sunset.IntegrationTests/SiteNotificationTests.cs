using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sunset.Application.Interfaces;
using Sunset.Infrastructure.SiteNotifications;

namespace Sunset.IntegrationTests;

// No database or server needed: these exercise the notifier against a fake HTTP handler.
public class SiteNotificationTests
{
    private sealed record Call(string Method, string Url, string? Secret, JsonElement Body);

    private sealed class RecordingHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _next;
        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = request.Content is null ? "null" : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("X-Revalidate-Secret", out var secret);
            lock (Calls)
                Calls.Add(new Call(request.Method.Method, request.RequestUri!.ToString(), secret?.SingleOrDefault(), JsonDocument.Parse(text).RootElement.Clone()));

            var status = statuses.Length == 0 ? HttpStatusCode.OK : statuses[Math.Min(_next++, statuses.Length - 1)];
            return new HttpResponseMessage(status);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static RevalidationOptions Site(string? secret = "a-secret-of-16-chars") => new() { BaseUrl = "https://sunset.example/", RevalidateSecret = secret };
    private static IndexNowOptions IndexNow(string? key = "abcdef1234567890") => new() { Key = key, Endpoint = "https://indexnow.test/indexnow" };

    private static SiteNotificationSender Sender(RecordingHandler handler, RevalidationOptions site, IndexNowOptions indexNow) =>
        new(new Factory(handler), Options.Create(site), Options.Create(indexNow), NullLogger<SiteNotificationSender>.Instance);

    [Fact]
    public async Task ABatch_BecomesOneRevalidateCall_WithTheUnionOfTagsAndDistinctPaths_AndTheSecret()
    {
        var handler = new RecordingHandler();
        var sender = Sender(handler, Site(), IndexNow(key: null));

        await sender.SendAsync(
        [
            new SiteNotification(ResponseCacheTag.Locations, new SiteChange(["/locations/a", "/cidades/x"])),
            new SiteNotification(ResponseCacheTag.Photos, new SiteChange(["/locations/a"])),
            new SiteNotification(ResponseCacheTag.Cities, null),
        ], default);

        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://sunset.example/api/revalidate", call.Url);
        Assert.Equal("a-secret-of-16-chars", call.Secret);
        Assert.Equal(["/locations/a", "/cidades/x"], call.Body.GetProperty("paths").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["locations", "photos", "cities"], call.Body.GetProperty("tags").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task IndexNow_IsToldOnlyAboutPagesMarkedAsNew_WithTheKeyFileLocation()
    {
        var handler = new RecordingHandler();
        var sender = Sender(handler, Site(secret: null), IndexNow());

        await sender.SendAsync(
        [
            new SiteNotification(ResponseCacheTag.Locations, new SiteChange(["/locations/new-one", "/cidades/x"], Index: true)),
            new SiteNotification(ResponseCacheTag.Photos, new SiteChange(["/locations/just-changed"], Index: false)),
        ], default);

        var call = Assert.Single(handler.Calls);   // no secret configured: no revalidate call at all
        Assert.Equal("https://indexnow.test/indexnow", call.Url);
        Assert.Equal("sunset.example", call.Body.GetProperty("host").GetString());
        Assert.Equal("abcdef1234567890", call.Body.GetProperty("key").GetString());
        Assert.Equal("https://sunset.example/abcdef1234567890.txt", call.Body.GetProperty("keyLocation").GetString());
        Assert.Equal(["https://sunset.example/locations/new-one", "https://sunset.example/cidades/x"],
            call.Body.GetProperty("urlList").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task NothingConfigured_SendsNothing()
    {
        var handler = new RecordingHandler();
        var sender = Sender(handler, Site(secret: null), IndexNow(key: null));

        await sender.SendAsync([new SiteNotification(ResponseCacheTag.All, new SiteChange(["/locations/a"], Index: true))], default);

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task AFailedDelivery_IsTriedOnceMore_AndAGiveUpNeverThrows()
    {
        var flaky = new RecordingHandler(HttpStatusCode.BadGateway, HttpStatusCode.OK);
        await Sender(flaky, Site(), IndexNow(key: null)).SendAsync([new SiteNotification(ResponseCacheTag.All, null)], default);
        Assert.Equal(2, flaky.Calls.Count);

        var down = new RecordingHandler(HttpStatusCode.InternalServerError);
        await Sender(down, Site(), IndexNow(key: null)).SendAsync([new SiteNotification(ResponseCacheTag.All, null)], default);
        Assert.Equal(2, down.Calls.Count);   // two attempts, then dropped quietly
    }

    [Fact]
    public void TheNotifier_IsANoOpUntilTheSiteOrIndexNowIsConfigured()
    {
        var off = new SiteNotifier(Options.Create(Site(secret: null)), Options.Create(IndexNow(key: null)));
        off.Enqueue(ResponseCacheTag.All);
        Assert.False(off.Reader.TryRead(out _));

        var on = new SiteNotifier(Options.Create(Site()), Options.Create(IndexNow(key: null)));
        on.Enqueue(ResponseCacheTag.All);
        on.Enqueue(ResponseCacheTag.None);   // nothing changed: nothing to tell
        Assert.True(on.Reader.TryRead(out var item));
        Assert.Equal(ResponseCacheTag.All, item!.Tags);
        Assert.False(on.Reader.TryRead(out _));
    }

    [Fact]
    public async Task TheWorker_TurnsABurstIntoOneCall()
    {
        var handler = new RecordingHandler();
        var notifier = new SiteNotifier(Options.Create(Site()), Options.Create(IndexNow(key: null)));
        var worker = new SiteNotifierWorker(notifier, Sender(handler, Site(), IndexNow(key: null)), NullLogger<SiteNotifierWorker>.Instance);

        await worker.StartAsync(default);
        notifier.Enqueue(ResponseCacheTag.Locations, new SiteChange(["/locations/a"]));
        notifier.Enqueue(ResponseCacheTag.Photos, new SiteChange(["/locations/b"]));
        notifier.Enqueue(ResponseCacheTag.Cities);

        for (var i = 0; i < 100 && handler.Calls.Count == 0; i++)
            await Task.Delay(100);
        await Task.Delay(300);
        await worker.StopAsync(default);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(["/locations/a", "/locations/b"], call.Body.GetProperty("paths").EnumerateArray().Select(e => e.GetString()));
    }
}
