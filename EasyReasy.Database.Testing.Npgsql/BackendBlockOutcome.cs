namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// The three ways a wait for one PostgreSQL backend to block on another can end. Distinguished rather
    /// than collapsed into a bool because they call for different reactions: only <see cref="Blocked"/>
    /// licenses a concurrency test's assertions, while the other two say different things about why the
    /// interleaving was not reached and so point at different mistakes in the test.
    /// </summary>
    public enum BackendBlockOutcome
    {
        /// <summary>
        /// The waiting backend was seen parked on the blocking backend. The interleaving under test was
        /// reached, so whatever the test asserts next is about a race that actually happened.
        /// </summary>
        Blocked,

        /// <summary>
        /// The in-flight work ran to completion without the waiting backend ever blocking. It got far
        /// enough to answer the question — it did not contend — so the wait ended there rather than
        /// burning the whole timeout. Usually means the test sequenced itself around the race.
        /// </summary>
        WorkFinishedWithoutBlocking,

        /// <summary>
        /// The timeout elapsed with the in-flight work still running and still not blocked. Usually means
        /// the work is waiting on something other than the intended backend, or is slow for an unrelated
        /// reason.
        /// </summary>
        TimedOut,
    }
}
