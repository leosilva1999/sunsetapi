using Sunset.Application.Push;

namespace Sunset.Application.Interfaces;

/// <summary>
/// Hands push messages to the provider (today Expo, which bridges to FCM and APNs). Retries and logs its own
/// failures: a failed delivery comes back as a <see cref="PushFailure"/>, never as an exception.
/// </summary>
public interface IPushSender
{
    /// <returns>One result per message, in the same order.</returns>
    Task<IReadOnlyList<PushSendResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>The final word on messages the provider had accepted, by ticket id; ids it does not know yet are left out.</summary>
    Task<IReadOnlyDictionary<string, PushReceipt>> GetReceiptsAsync(IReadOnlyCollection<string> ticketIds, CancellationToken cancellationToken = default);
}
