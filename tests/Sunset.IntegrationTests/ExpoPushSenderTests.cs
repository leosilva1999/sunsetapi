using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sunset.Application.Push;
using Sunset.Infrastructure.Push;

namespace Sunset.IntegrationTests;

// No database or server needed: the sender talks to a scripted HTTP handler.
public class ExpoPushSenderTests
{
    private sealed record Call(string Path, string? Authorization, JsonElement Body);

    private sealed class ScriptedHandler(Func<int, HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = await request.Content!.ReadAsStringAsync(cancellationToken);
            int n;
            lock (Calls)
            {
                Calls.Add(new Call(request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), JsonDocument.Parse(text).RootElement.Clone()));
                n = Calls.Count;
            }

            return reply(n, request);
        }
    }

    private sealed class CapturingLogger : ILogger<ExpoPushSender>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{logLevel}: {formatter(state, exception)}");
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static PushMessage Message(string token) =>
        new(token, "Nova curtida", "Ana curtiu sua foto", 2, new Dictionary<string, object> { ["v"] = 1, ["type"] = "PhotoLiked" });

    private static ExpoPushSender Sender(ScriptedHandler handler, CapturingLogger? logger = null, string? accessToken = null, bool dryRun = false) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://expo.test/") },
            new PushSettings { Enabled = true, ExpoAccessToken = accessToken, DryRun = dryRun, RetryDelayMs = 1 },
            logger ?? new CapturingLogger());

    private static string OkFor(int count) =>
        "{\"data\":[" + string.Join(",", Enumerable.Range(0, count).Select(i => $"{{\"status\":\"ok\",\"id\":\"ticket-{i}\"}}")) + "]}";

    [Fact]
    public async Task AMessageGoesOutInExpoFormat_WithTheBearerTokenWhenOneIsConfigured()
    {
        var handler = new ScriptedHandler((_, _) => Json(OkFor(1)));

        var results = await Sender(handler, accessToken: "secret-access-token").SendAsync([Message("ExponentPushToken[aaa]")]);

        var call = Assert.Single(handler.Calls);
        Assert.Equal("/--/api/v2/push/send", call.Path);
        Assert.Equal("Bearer secret-access-token", call.Authorization);
        var sent = call.Body[0];
        Assert.Equal("ExponentPushToken[aaa]", sent.GetProperty("to").GetString());
        Assert.Equal("Nova curtida", sent.GetProperty("title").GetString());
        Assert.Equal("Ana curtiu sua foto", sent.GetProperty("body").GetString());
        Assert.Equal(2, sent.GetProperty("badge").GetInt32());
        Assert.Equal("high", sent.GetProperty("priority").GetString());
        Assert.Equal("default", sent.GetProperty("channelId").GetString());
        Assert.Equal(86400, sent.GetProperty("ttl").GetInt32());
        Assert.Equal("PhotoLiked", sent.GetProperty("data").GetProperty("type").GetString());
        Assert.Equal(PushSendResult.Accepted("ticket-0"), Assert.Single(results));
    }

    [Fact]
    public async Task WithoutAConfiguredToken_NoAuthorizationHeaderIsSent()
    {
        var handler = new ScriptedHandler((_, _) => Json(OkFor(1)));

        await Sender(handler).SendAsync([Message("ExponentPushToken[aaa]")]);

        Assert.Null(handler.Calls.Single().Authorization);
    }

    [Fact]
    public async Task MoreThanAHundredMessages_AreSplitIntoBatchesOfAHundred_KeepingTheOrderOfTheResults()
    {
        var handler = new ScriptedHandler((_, request) =>
        {
            var count = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement.GetArrayLength();
            return Json(OkFor(count));
        });
        var messages = Enumerable.Range(0, 250).Select(i => Message($"ExponentPushToken[{i}]")).ToList();

        var results = await Sender(handler).SendAsync(messages);

        Assert.Equal([100, 100, 50], handler.Calls.Select(c => c.Body.GetArrayLength()));
        Assert.Equal(250, results.Count);
        Assert.All(results, r => Assert.Equal(PushFailure.None, r.Failure));
    }

    [Fact]
    public async Task EachTicketIsReadOnItsOwn_ADeadTokenAmongGoodOnes()
    {
        var handler = new ScriptedHandler((_, _) => Json("""
            {"data":[
              {"status":"ok","id":"t1"},
              {"status":"error","message":"not registered","details":{"error":"DeviceNotRegistered"}},
              {"status":"error","message":"slow down","details":{"error":"MessageRateExceeded"}},
              {"status":"error","message":"too big","details":{"error":"MessageTooBig"}}
            ]}
            """));

        var results = await Sender(handler).SendAsync(Enumerable.Range(0, 4).Select(i => Message($"ExponentPushToken[{i}]")).ToList());

        Assert.Equal(
            [PushFailure.None, PushFailure.DeviceNotRegistered, PushFailure.Transient, PushFailure.Rejected],
            results.Select(r => r.Failure));
        Assert.Equal("t1", results[0].TicketId);
    }

    [Fact]
    public async Task AServerErrorIsRetried_AndASuccessAfterwardsCounts()
    {
        var handler = new ScriptedHandler((n, _) => n < 3 ? Json("{}", HttpStatusCode.BadGateway) : Json(OkFor(1)));

        var results = await Sender(handler).SendAsync([Message("ExponentPushToken[aaa]")]);

        Assert.Equal(3, handler.Calls.Count);
        Assert.Equal(PushFailure.None, Assert.Single(results).Failure);
    }

    [Fact]
    public async Task AfterThreeFailedAttempts_ItGivesUp_WithoutThrowing()
    {
        var handler = new ScriptedHandler((_, _) => Json("{}", HttpStatusCode.ServiceUnavailable));

        var results = await Sender(handler).SendAsync([Message("ExponentPushToken[aaa]"), Message("ExponentPushToken[bbb]")]);

        Assert.Equal(3, handler.Calls.Count);
        Assert.All(results, r => Assert.Equal(PushFailure.Transient, r.Failure));
    }

    [Fact]
    public async Task ARefusalIsNotRetried()
    {
        var handler = new ScriptedHandler((_, _) => Json("""{"errors":[{"code":"UNAUTHORIZED"}]}""", HttpStatusCode.Unauthorized));

        var results = await Sender(handler).SendAsync([Message("ExponentPushToken[aaa]")]);

        Assert.Single(handler.Calls);
        Assert.Equal(PushFailure.Rejected, Assert.Single(results).Failure);
    }

    [Fact]
    public async Task ABodyThatDoesNotMatchTheBatch_IsTreatedAsRejected_NotTrusted()
    {
        var handler = new ScriptedHandler((_, _) => Json(OkFor(1)));

        var results = await Sender(handler).SendAsync([Message("ExponentPushToken[a]"), Message("ExponentPushToken[b]")]);

        Assert.All(results, r => Assert.Equal(PushFailure.Rejected, r.Failure));
    }

    [Fact]
    public async Task Logs_NeverContainAWholePushToken_NorTheAccessToken()
    {
        var logger = new CapturingLogger();
        var handler = new ScriptedHandler((_, _) => Json("""
            {"data":[{"status":"error","message":"boom","details":{"error":"InvalidCredentials"}}]}
            """));

        await Sender(handler, logger, accessToken: "secret-access-token").SendAsync([Message("ExponentPushToken[abcdefghijklmnop]")]);

        var log = string.Join("\n", logger.Lines);
        Assert.Contains("InvalidCredentials", log);
        Assert.DoesNotContain("abcdefghijklmnop", log);
        Assert.DoesNotContain("ExponentPushToken", log);
        Assert.DoesNotContain("secret-access-token", log);
    }

    [Fact]
    public async Task ADryRun_LogsInsteadOfSending()
    {
        var logger = new CapturingLogger();
        var handler = new ScriptedHandler((_, _) => Json(OkFor(1)));

        var results = await Sender(handler, logger, dryRun: true).SendAsync([Message("ExponentPushToken[abcdefghijklmnop]")]);

        Assert.Empty(handler.Calls);
        Assert.Equal(PushFailure.None, Assert.Single(results).Failure);
        Assert.Contains(logger.Lines, l => l.Contains("dry run") && l.Contains("Ana curtiu sua foto") && !l.Contains("abcdefghijklmnop"));
    }

    [Fact]
    public async Task Receipts_AreMapped_AndIdsTheProviderDoesNotKnowYetAreLeftOut()
    {
        var handler = new ScriptedHandler((_, _) => Json("""
            {"data":{
              "r-ok":{"status":"ok"},
              "r-dead":{"status":"error","message":"gone","details":{"error":"DeviceNotRegistered"}},
              "r-other":{"status":"error","message":"nope","details":{"error":"MessageTooBig"}}
            }}
            """));

        var receipts = await Sender(handler).GetReceiptsAsync(["r-ok", "r-dead", "r-other", "r-pending"]);

        var call = Assert.Single(handler.Calls);
        Assert.Equal("/--/api/v2/push/getReceipts", call.Path);
        Assert.Equal(4, call.Body.GetProperty("ids").GetArrayLength());
        Assert.Equal(new PushReceipt(true, PushFailure.None), receipts["r-ok"]);
        Assert.Equal(new PushReceipt(false, PushFailure.DeviceNotRegistered), receipts["r-dead"]);
        Assert.Equal(new PushReceipt(false, PushFailure.Rejected), receipts["r-other"]);
        Assert.DoesNotContain("r-pending", receipts.Keys);
    }

    [Fact]
    public async Task WhenReceiptsCannotBeFetched_NothingIsReported_SoTheyAreAskedAgainLater()
    {
        var handler = new ScriptedHandler((_, _) => Json("{}", HttpStatusCode.InternalServerError));

        var receipts = await Sender(handler).GetReceiptsAsync(["a"]);

        Assert.Empty(receipts);
    }

    [Fact]
    public void TheNotifier_IsANoOpWhilePushIsOff()
    {
        var off = new PushNotifier(new PushSettings { Enabled = false });
        off.Enqueue(new PushRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sunset.Domain.Enums.NotificationType.PhotoLiked, "Photo:1"));
        Assert.False(off.Reader.TryRead(out _));

        var on = new PushNotifier(new PushSettings { Enabled = true });
        on.Enqueue(new PushRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sunset.Domain.Enums.NotificationType.PhotoLiked, "Photo:1"));
        Assert.True(on.Reader.TryRead(out var item));
        Assert.Equal("Photo:1", item!.TargetDescription);
    }
}
