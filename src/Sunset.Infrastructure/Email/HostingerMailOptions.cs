namespace Sunset.Infrastructure.Email;

public class HostingerMailOptions
{
    public const string SectionName = "Hostinger:Mail";

    public string BaseUrl { get; init; } = "https://api.mail.hostinger.com/";
    public string ApiKey { get; init; } = null!;

    // The token is bound to one managed mailbox; this is that mailbox's resource id (not its
    // address - the address is the From, decided by Hostinger from the mailbox itself).
    public string MailboxResourceId { get; init; } = null!;

    // Shown in the From header next to the mailbox address.
    public string? DisplayName { get; init; }
}
