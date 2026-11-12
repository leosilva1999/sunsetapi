using System.Threading.Channels;
using Sunset.Application.Interfaces;
using Sunset.Application.Push;

namespace Sunset.Infrastructure.Push;

/// <summary>
/// The in-memory queue behind <see cref="IPushNotifier"/>. Bounded, dropping the oldest request when full (the
/// notification is in the app anyway), and a no-op while push is switched off.
/// </summary>
public sealed class PushNotifier(PushSettings settings) : IPushNotifier
{
    private readonly Channel<PushRequest> _channel = Channel.CreateBounded<PushRequest>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public System.Threading.Channels.ChannelReader<PushRequest> Reader => _channel.Reader;

    public void Enqueue(PushRequest request)
    {
        if (settings.Enabled)
            _channel.Writer.TryWrite(request);
    }
}
