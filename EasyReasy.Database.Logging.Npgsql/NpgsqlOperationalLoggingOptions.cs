namespace EasyReasy.Database.Logging.Npgsql
{
    /// <summary>
    /// PostgreSQL-specific options layered on top of the core operational logging configuration.
    /// </summary>
    public sealed class NpgsqlOperationalLoggingOptions
    {
        /// <summary>
        /// How long to keep operational log rows. The partition-maintenance service drops monthly
        /// partitions entirely older than this. Defaults to 90 days.
        /// </summary>
        public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);

        /// <summary>
        /// Whether to run the background partition-maintenance service (create current + 2 future
        /// monthly partitions, drop partitions older than <see cref="Retention"/>). Defaults to
        /// <c>true</c>. Set to <c>false</c> if partitions are maintained out-of-band.
        /// </summary>
        public bool MaintainPartitions { get; set; } = true;
    }
}
