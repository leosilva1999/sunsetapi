using Sunset.Domain.Enums;

namespace Sunset.Application.Push;

/// <summary>Several notifications of one kind for one person, collected during a window and sent as a single push.</summary>
/// <param name="Latest">The most recent one: its ids are what the push opens.</param>
/// <param name="FirstActorId">Who did it first; named in the text.</param>
/// <param name="OtherActors">How many DIFFERENT other people did it too.</param>
public sealed record PushGroup(PushRequest Latest, Guid FirstActorId, int OtherActors);

/// <summary>
/// Keeps a burst of likes (or comments, followers...) from becoming a burst of pushes. The first notification for a
/// key (person, kind, target) goes out at once; the ones that arrive during the next window are held and leave
/// together when it ends, after which a new window opens. In memory: there is one API instance, and a restart that
/// drops the held ones loses only pushes (the notifications stay in the app).
/// </summary>
public sealed class PushGrouper(PushSettings settings)
{
    private const int MaxHeldPerKey = 1000;

    private sealed class Entry
    {
        public DateTime WindowEnd { get; set; }
        public PushRequest? Latest { get; set; }
        public List<Guid> Actors { get; } = [];
    }

    private readonly Dictionary<(Guid Recipient, NotificationType Type, string Target), Entry> _entries = [];
    private readonly object _lock = new();

    // A new follower's target is that follower ("User:{id}"), different every time: all followers of a person are one group.
    private static (Guid, NotificationType, string) KeyOf(PushRequest request) =>
        (request.RecipientUserId, request.Type, request.Type == NotificationType.NewFollower ? string.Empty : request.TargetDescription);

    /// <summary>True: send this one now (and a window opens). False: it was held for the group that leaves when the window ends.</summary>
    public bool OfferSendNow(PushRequest request, DateTime utcNow)
    {
        if (settings.GroupWindowSeconds <= 0)
            return true;

        lock (_lock)
        {
            var key = KeyOf(request);
            if (!_entries.TryGetValue(key, out var entry))
                _entries[key] = entry = new Entry();

            if (entry.WindowEnd <= utcNow && entry.Latest is null)
            {
                entry.WindowEnd = utcNow.AddSeconds(settings.GroupWindowSeconds);
                return true;
            }

            entry.Latest = request;
            if (!entry.Actors.Contains(request.ActorUserId) && entry.Actors.Count < MaxHeldPerKey)
                entry.Actors.Add(request.ActorUserId);
            return false;
        }
    }

    /// <summary>The groups whose window has ended. Each one opens a new window; windows with nothing held are forgotten.</summary>
    public IReadOnlyList<PushGroup> TakeDue(DateTime utcNow)
    {
        lock (_lock)
        {
            var due = new List<PushGroup>();
            foreach (var (key, entry) in _entries.ToList())
            {
                if (entry.WindowEnd > utcNow)
                    continue;

                if (entry.Latest is null)
                {
                    _entries.Remove(key);
                    continue;
                }

                due.Add(new PushGroup(entry.Latest, entry.Actors[0], entry.Actors.Count - 1));
                entry.Latest = null;
                entry.Actors.Clear();
                entry.WindowEnd = utcNow.AddSeconds(settings.GroupWindowSeconds);
            }

            return due;
        }
    }
}
