using System.Collections.Concurrent;
using System.Threading.Channels;

namespace EasyReasy.Database.Logging.Broadcasting
{
    /// <summary>
    /// In-memory base broadcaster that fans out every published event to all current subscribers
    /// via bounded channels. In-process only — a multi-instance deployment would need an
    /// out-of-process transport (e.g. Postgres <c>LISTEN/NOTIFY</c>). Each subscriber's channel
    /// drops the oldest buffered event when full, so a slow reader can never back-pressure the
    /// publisher.
    /// </summary>
    /// <typeparam name="TEvent">The event type being broadcast.</typeparam>
    public abstract class InMemoryFanoutBroadcaster<TEvent>
    {
        // Per-subscriber buffer depth. Bounded + DropOldest means a slow reader loses old events
        // rather than stalling the sink that publishes to every subscriber.
        private const int ChannelCapacity = 1000;

        private readonly ConcurrentDictionary<ChannelReader<TEvent>, Channel<TEvent>> _subscribers = new();

        /// <summary>
        /// Publishes an event to every current subscriber. Drops the event for any subscriber whose
        /// channel is full.
        /// </summary>
        public void Publish(TEvent eventToPublish)
        {
            foreach (KeyValuePair<ChannelReader<TEvent>, Channel<TEvent>> entry in _subscribers)
            {
                entry.Value.Writer.TryWrite(eventToPublish);
            }
        }

        /// <summary>Creates a new subscription and returns a channel reader for receiving events.</summary>
        public ChannelReader<TEvent> Subscribe()
        {
            Channel<TEvent> channel = Channel.CreateBounded<TEvent>(
                new BoundedChannelOptions(ChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                });

            _subscribers.TryAdd(channel.Reader, channel);

            return channel.Reader;
        }

        /// <summary>Removes a subscription and completes its channel.</summary>
        public void Unsubscribe(ChannelReader<TEvent> reader)
        {
            if (_subscribers.TryRemove(reader, out Channel<TEvent>? channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }
}
