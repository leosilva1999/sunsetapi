namespace Sunset.Infrastructure.SiteNotifications;

/// <summary>The shared secret the site expects in X-Revalidate-Secret; empty turns revalidation off.</summary>
public class RevalidationOptions
{
    public const string SectionName = "Frontend";

    public string? BaseUrl { get; init; }

    public string? RevalidateSecret { get; init; }
}

/// <summary>The IndexNow key (the site serves {Key}.txt at its root); empty turns IndexNow off.</summary>
public class IndexNowOptions
{
    public const string SectionName = "IndexNow";

    public string? Key { get; init; }

    public string Endpoint { get; init; } = "https://api.indexnow.org/indexnow";
}
