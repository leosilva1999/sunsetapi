using System.Text.Json.Serialization;

namespace Sunset.Domain.Enums;

/// <summary>Service: things people need to know (terms, safety, maintenance). Promotional: everything else; needs the person's consent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnnouncementCategory
{
    Service,
    Promotional
}

/// <summary>Who receives an announcement.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnnouncementAudience
{
    /// <summary>Every active account.</summary>
    All,

    /// <summary>Specific accounts, listed by id.</summary>
    Users,

    /// <summary>Accounts with the app registered for push (at least one device).</summary>
    PushEnabled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnnouncementStatus
{
    /// <summary>Saved, not started yet.</summary>
    Pending,

    /// <summary>The background worker is creating the notifications.</summary>
    Sending,

    /// <summary>Every recipient has their notification.</summary>
    Sent
}
