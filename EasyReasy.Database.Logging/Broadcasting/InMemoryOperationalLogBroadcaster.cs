using EasyReasy.Database.Logging.Models;

namespace EasyReasy.Database.Logging.Broadcasting
{
    /// <summary>
    /// In-memory singleton <see cref="IOperationalLogBroadcaster"/> that fans out operational log
    /// rows to all connected subscribers via bounded channels. In-process only.
    /// </summary>
    public sealed class InMemoryOperationalLogBroadcaster
        : InMemoryFanoutBroadcaster<OperationalLogEvent>, IOperationalLogBroadcaster
    {
    }
}
