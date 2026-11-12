using Sunset.Application.Push;

namespace Sunset.Application.Interfaces;

/// <summary>
/// Asks for a push after a notification was saved. Fire-and-forget by design: it never waits and never throws,
/// so the like/comment/follow that caused it is never slowed or broken by the push channel.
/// </summary>
public interface IPushNotifier
{
    void Enqueue(PushRequest request);
}
