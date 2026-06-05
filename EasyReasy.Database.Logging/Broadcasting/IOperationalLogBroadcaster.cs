using System.Threading.Channels;
using EasyReasy.Database.Logging.Models;

namespace EasyReasy.Database.Logging.Broadcasting
{
    /// <summary>
    /// Fans out operational log events to in-process subscribers (e.g. a live admin feed). The
    /// sink publishes every row that lands in the database; any current subscriber receives it.
    /// Unscoped — there is no per-user or per-tenant key.
    /// </summary>
    public interface IOperationalLogBroadcaster
    {
        /// <summary>Publishes an operational log event to every current subscriber.</summary>
        void Publish(OperationalLogEvent logEvent);

        /// <summary>Creates a new subscription and returns a channel reader for receiving events.</summary>
        ChannelReader<OperationalLogEvent> Subscribe();

        /// <summary>Removes a subscription and completes its channel.</summary>
        void Unsubscribe(ChannelReader<OperationalLogEvent> reader);
    }
}
