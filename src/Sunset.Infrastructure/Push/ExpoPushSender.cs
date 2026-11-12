using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sunset.Application.Interfaces;
using Sunset.Application.Push;

namespace Sunset.Infrastructure.Push;

/// <summary>
/// Sends through Expo's push service (<c>/--/api/v2/push/send</c>), which bridges to FCM and APNs, so the API holds no
/// Firebase or Apple credentials. Batches of 100, up to 3 attempts per batch on network errors, 429 and 5xx, with
/// back-off. Never throws for a delivery problem; tokens appear in logs only masked.
/// </summary>
public sealed class ExpoPushSender(HttpClient httpClient, PushSettings settings, ILogger<ExpoPushSender> logger) : IPushSender
{
    public const int SendBatchSize = 100;
    public const int ReceiptBatchSize = 1000;
    private const int MaxAttempts = 3;

    public async Task<IReadOnlyList<PushSendResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default)
    {
        var results = new List<PushSendResult>(messages.Count);

        if (settings.DryRun)
        {
            foreach (var message in messages)
                logger.LogInformation("[push dry run] to {Token}: {Title} - {Body}", PushTokens.Mask(message.To), message.Title, message.Body);
            return messages.Select(_ => new PushSendResult(null, PushFailure.None)).ToList();
        }

        foreach (var batch in messages.Chunk(SendBatchSize))
            results.AddRange(await SendBatchAsync(batch, cancellationToken));

        return results;
    }

    private async Task<IReadOnlyList<PushSendResult>> SendBatchAsync(PushMessage[] batch, CancellationToken cancellationToken)
    {
        var body = batch.Select(m => new
        {
            to = m.To,
            title = m.Title,
            body = m.Body,
            sound = "default",
            channelId = "default",
            priority = "high",
            ttl = 86400,
            badge = m.Badge,
            data = m.Data,
        }).ToList();

        var response = await PostWithRetriesAsync("/--/api/v2/push/send", body, cancellationToken);
        if (response is null)
            return batch.Select(_ => PushSendResult.Failed(PushFailure.Transient)).ToList();

        if (response.Value.Status is not null)
        {
            // The whole request was refused (bad credentials, malformed body...): not worth repeating.
            logger.LogError("Expo refused a push batch of {Count}: HTTP {Status}", batch.Length, response.Value.Status);
            return batch.Select(_ => PushSendResult.Failed(PushFailure.Rejected)).ToList();
        }

        using var doc = response.Value.Document!;
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != batch.Length)
        {
            logger.LogError("Expo answered a push batch of {Count} with an unexpected body", batch.Length);
            return batch.Select(_ => PushSendResult.Failed(PushFailure.Rejected)).ToList();
        }

        var results = new List<PushSendResult>(batch.Length);
        for (var i = 0; i < batch.Length; i++)
        {
            var ticket = data[i];
            var status = ticket.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (status == "ok" && ticket.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } ticketId)
            {
                results.Add(PushSendResult.Accepted(ticketId));
                continue;
            }

            var error = ErrorCode(ticket);
            var failure = ToFailure(error);
            if (failure != PushFailure.DeviceNotRegistered)
                LogProblem(error, PushTokens.Mask(batch[i].To));
            results.Add(PushSendResult.Failed(failure));
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, PushReceipt>> GetReceiptsAsync(IReadOnlyCollection<string> ticketIds, CancellationToken cancellationToken = default)
    {
        var receipts = new Dictionary<string, PushReceipt>();
        if (settings.DryRun)
            return receipts;

        foreach (var batch in ticketIds.Chunk(ReceiptBatchSize))
        {
            var response = await PostWithRetriesAsync("/--/api/v2/push/getReceipts", new { ids = batch }, cancellationToken);
            if (response is null || response.Value.Status is not null)
                continue;   // asked again at the next round

            using var doc = response.Value.Document!;
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var entry in data.EnumerateObject())
            {
                var status = entry.Value.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (status == "ok")
                {
                    receipts[entry.Name] = new PushReceipt(true, PushFailure.None);
                    continue;
                }

                var error = ErrorCode(entry.Value);
                var failure = ToFailure(error);
                if (failure != PushFailure.DeviceNotRegistered)
                    LogProblem(error, "(receipt)");
                receipts[entry.Name] = new PushReceipt(false, failure);
            }
        }

        return receipts;
    }

    private static string? ErrorCode(JsonElement ticket) =>
        ticket.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object && details.TryGetProperty("error", out var e)
            ? e.GetString()
            : null;

    private static PushFailure ToFailure(string? error) => error switch
    {
        "DeviceNotRegistered" => PushFailure.DeviceNotRegistered,
        "MessageRateExceeded" => PushFailure.Transient,
        _ => PushFailure.Rejected,
    };

    private void LogProblem(string? error, string maskedToken)
    {
        if (error == "InvalidCredentials")
            logger.LogError("Expo rejected a push for {Token}: InvalidCredentials - check the FCM/APNs credentials in EAS", maskedToken);
        else
            logger.LogWarning("Expo did not deliver a push to {Token}: {Error}", maskedToken, error ?? "unknown");
    }

    private readonly record struct Reply(int? Status, JsonDocument? Document);

    // A 2xx answer is returned parsed; a definitive refusal (4xx other than 429) as its status; null = gave up after retries.
    private async Task<Reply?> PostWithRetriesAsync(string path, object body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(settings.ExpoAccessToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ExpoAccessToken);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return new Reply(null, await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken));

                if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                    return new Reply((int)response.StatusCode, null);

                logger.LogWarning("Expo answered {Status} (attempt {Attempt}/{Max})", (int)response.StatusCode, attempt, MaxAttempts);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not reach Expo (attempt {Attempt}/{Max})", attempt, MaxAttempts);
            }

            if (attempt >= MaxAttempts)
                return null;

            await Task.Delay(TimeSpan.FromMilliseconds(settings.RetryDelayMs * Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }
}
