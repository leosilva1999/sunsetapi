using System.Text.RegularExpressions;
using Sunset.Domain.Enums;

namespace Sunset.Application.Push;

public static partial class PushTokens
{
    public const int MaxLength = 255;

    [GeneratedRegex(@"^(Exponent|Expo)PushToken\[.+\]$")]
    private static partial Regex ExpoTokenRegex();

    public static bool IsValid(string? token) =>
        !string.IsNullOrEmpty(token) && token.Length <= MaxLength && ExpoTokenRegex().IsMatch(token);

    /// <summary>For logs: a push token is a device identifier, so only its last characters are shown.</summary>
    public static string Mask(string? token) =>
        string.IsNullOrEmpty(token) ? "(none)" : token.Length <= 8 ? "…" : "…" + token[^7..^1];
}

/// <summary>The text of each push (pt-BR), composed on the server so the app shows it as it comes.</summary>
public static class PushMessageComposer
{
    public const int PayloadVersion = 1;

    /// <param name="otherActors">0 for a single notification; otherwise how many MORE people did it ("Ana e mais 3 curtiram...").</param>
    public static PushMessage Compose(string token, PushRequest request, string actorName, int unreadCount, int otherActors = 0)
    {
        var (title, body) = otherActors <= 0
            ? request.Type switch
            {
                NotificationType.NewFollower => ("Novo seguidor", $"{actorName} começou a seguir você"),
                NotificationType.PhotoLiked => ("Nova curtida", $"{actorName} curtiu sua foto"),
                NotificationType.PhotoCommented => ("Novo comentário", $"{actorName} comentou na sua foto"),
                NotificationType.CommentReplied => ("Nova resposta", $"{actorName} respondeu seu comentário"),
                _ => ("Sunset", "Você tem uma nova notificação"),
            }
            : request.Type switch
            {
                NotificationType.NewFollower => ("Novos seguidores", $"{actorName} e mais {otherActors} começaram a seguir você"),
                NotificationType.PhotoLiked => ("Novas curtidas", $"{actorName} e mais {otherActors} curtiram sua foto"),
                NotificationType.PhotoCommented => ("Novos comentários", $"{actorName} e mais {otherActors} comentaram na sua foto"),
                NotificationType.CommentReplied => ("Novas respostas", $"{actorName} e mais {otherActors} responderam seu comentário"),
                _ => ("Sunset", "Você tem novas notificações"),
            };

        // Only ids and labels travel in `data`: the text itself is the only thing a lock screen shows.
        var data = new Dictionary<string, object>
        {
            ["v"] = PayloadVersion,
            ["notificationId"] = request.NotificationId,
            ["type"] = request.Type.ToString(),
            ["actorUserId"] = request.ActorUserId,
            ["targetDescription"] = request.TargetDescription,
        };

        // A grouped push says how many people it stands for; the ids are those of the latest notification.
        if (otherActors > 0)
            data["count"] = otherActors + 1;

        return new PushMessage(token, title, body, unreadCount, data);
    }
}

/// <summary>
/// A person gets at most N pushes per hour (sliding window); the rest stay in-app only, so a burst of likes
/// is not spam. In memory: there is one API instance, and losing the counters on a restart only forgives a few.
/// </summary>
public sealed class PushThrottle(PushSettings settings)
{
    private readonly Dictionary<Guid, Queue<DateTime>> _sent = [];
    private readonly object _lock = new();

    public bool TryAcquire(Guid userId, DateTime utcNow)
    {
        lock (_lock)
        {
            if (!_sent.TryGetValue(userId, out var times))
                _sent[userId] = times = new Queue<DateTime>();

            while (times.Count > 0 && times.Peek() <= utcNow.AddHours(-1))
                times.Dequeue();

            if (times.Count >= settings.MaxPushesPerUserPerHour)
                return false;

            times.Enqueue(utcNow);
            return true;
        }
    }

    /// <summary>Forgets people with nothing left in the window, so the table does not grow without bound.</summary>
    public void Trim(DateTime utcNow)
    {
        lock (_lock)
        {
            var cutoff = utcNow.AddHours(-1);
            foreach (var id in _sent.Where(e => e.Value.Count == 0 || e.Value.Max() <= cutoff).Select(e => e.Key).ToList())
                _sent.Remove(id);
        }
    }
}
