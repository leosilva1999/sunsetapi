namespace Sunset.Domain.Entities;

/// <summary>
/// The receipt-to-be of one push the provider accepted. About 15 minutes later the provider can say the
/// delivery failed (above all "DeviceNotRegistered"), and then <see cref="Token"/> is removed. Kept by token,
/// not by device row: the device may have been deleted or moved in the meantime. Short-lived.
/// </summary>
public class PushTicket : BaseEntity
{
    /// <summary>The provider's id for the accepted message, the key to ask for its receipt.</summary>
    public string ProviderTicketId { get; private set; } = null!;

    public string Token { get; private set; } = null!;

    private PushTicket() { }

    public PushTicket(string providerTicketId, string token)
    {
        if (string.IsNullOrWhiteSpace(providerTicketId))
            throw new ArgumentException("ProviderTicketId is required.", nameof(providerTicketId));
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token is required.", nameof(token));

        ProviderTicketId = providerTicketId;
        Token = token;
    }
}
