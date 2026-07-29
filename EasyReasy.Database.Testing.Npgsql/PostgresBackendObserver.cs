using System.Diagnostics;
using System.Globalization;
using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// Reads one PostgreSQL backend's view of another over a separate connection — the lock-wait
    /// observation a concurrency test needs in order to prove it actually reached the interleaving it is
    /// about, rather than sequencing itself around it and asserting on a race that never happened.
    /// <para>
    /// The gap this closes is not theoretical. A test that fires its tasks and waits for all of them
    /// passes whether or not the two statements ever overlapped, and the obvious shape for a
    /// read-then-act race — mutating between the two calls — passes against the UNFIXED code, because the
    /// racing statement then runs against a snapshot that already includes the commit it was supposed to
    /// race. Such a test looks like it proves a fix and proves nothing. The only reliable signal is the
    /// database's own: one backend parked on another backend's transaction.
    /// </para>
    /// <para>
    /// The observation has to happen while the wait is in progress, so it cannot run over the WAITING
    /// connection — that one is parked and will not answer until the wait it is being asked about has
    /// already ended. The blocking connection is merely idle in its transaction and would in fact answer
    /// correctly, but a third connection is what a caller should use: it keeps the observation off the
    /// connections under test, where an extra round trip is one more thing perturbing the interleaving.
    /// </para>
    /// </summary>
    public static class PostgresBackendObserver
    {
        /// <summary>
        /// How often the observer asks. Sampling, not tracing: a wait that forms and clears entirely
        /// between two polls is missed, and then reported as though it never happened. That is tolerable
        /// only because the contention these tests set up is held open deliberately — by a transaction the
        /// test itself controls — rather than being raced for.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

        /// <summary>
        /// Both pids are pinned deliberately. Asking only whether <em>something</em> is blocked by the
        /// blocking backend is a weaker question with the same shape: an unrelated statement queueing
        /// behind the same lock satisfies it, and the caller would then treat an interleaving it never
        /// reached as proven. Dropping the <c>pg_blocking_pids</c> half is worse still — the count is then
        /// non-zero whenever the waiting backend has any <c>pg_stat_activity</c> row at all, so every test
        /// resting on this silently stops proving anything while staying green.
        /// </summary>
        private const string IsBlockedByQuery =
            "SELECT COUNT(*) FROM pg_stat_activity WHERE pid = @waitingPid AND @blockingPid = ANY(pg_blocking_pids(pid))";

        /// <summary>
        /// Waits until <paramref name="waitingBackendPid"/> is seen blocked by
        /// <paramref name="blockingBackendPid"/>, and reports what happened.
        /// <para>
        /// <paramref name="inFlightWork"/> is watched alongside the poll. Work that runs to completion
        /// without ever waiting has answered the question — it did not block — so the wait ends there
        /// instead of burning the full timeout.
        /// </para>
        /// </summary>
        /// <param name="observer">
        /// An open connection separate from both contending ones, used only to read the catalog. Any
        /// database in the cluster will do: pids and <c>pg_stat_activity</c> are cluster-wide. Both
        /// contending connections must stay OPEN for the whole call — a pid is only meaningful while its
        /// backend is alive, and a closed connection's pid reads exactly like a backend that never waited.
        /// </param>
        /// <param name="waitingBackendPid">
        /// The backend expected to park, from <see cref="NpgsqlConnection.ProcessID"/> of its connection.
        /// </param>
        /// <param name="blockingBackendPid">
        /// The backend expected to hold it up, from <see cref="NpgsqlConnection.ProcessID"/> of its
        /// connection.
        /// </param>
        /// <param name="inFlightWork">The task running the statement expected to park.</param>
        /// <param name="timeout">
        /// How long to keep asking before giving up. Each individual query is bounded by whatever is left
        /// of it, rather than left at Npgsql's 30 second default, so a stalled observer connection cannot
        /// quietly run past the budget. The timed-out path then adds two diagnostic reads under the same
        /// bound, so a wait that gives up is the only one that can exceed the budget, and only by that.
        /// </param>
        /// <param name="cancellationToken">Abandons the wait; does not produce an observation.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is zero or negative, which would return an observation without a
        /// single poll having run.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The catalog query returned something other than the bigint <c>COUNT(*)</c> produces. Deliberately
        /// not folded into a "not blocked yet" answer: the caller would then burn its whole timeout and
        /// report that the interleaving was never reached — a confident wrong diagnosis of a test that may
        /// be working perfectly.
        /// </exception>
        public static async Task<BackendBlockObservation> WaitForBlockAsync(
            NpgsqlConnection observer,
            int waitingBackendPid,
            int blockingBackendPid,
            Task inFlightWork,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(observer);
            ArgumentNullException.ThrowIfNull(inFlightWork);

            // A budget that cannot fit a single poll would return "never blocked" without ever having
            // asked — an observation the caller cannot tell from a real one, reporting that the
            // interleaving was not reached when nothing looked.
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout), timeout, "The timeout must be positive, so that at least one poll happens.");
            }

            // A monotonic Stopwatch clock rather than DateTime.UtcNow, which a system-clock adjustment
            // mid-run can move in either direction.
            long start = Stopwatch.GetTimestamp();

            TimeSpan remaining;

            while ((remaining = timeout - Stopwatch.GetElapsedTime(start)) > TimeSpan.Zero)
            {
                // Bounded by what is LEFT of the budget rather than by the whole of it, so a poll that
                // stalls late in a long wait cannot double the call's duration.
                if (await IsBlockedByAsync(observer, waitingBackendPid, blockingBackendPid, remaining, cancellationToken))
                {
                    return BackendBlockObservation.Blocked(waitingBackendPid, blockingBackendPid);
                }

                // IsCompleted covers faulted and cancelled too, hence "finished" rather than "succeeded" —
                // all three mean it got far enough to stop waiting, which is the thing being ruled out here.
                if (inFlightWork.IsCompleted)
                {
                    return BackendBlockObservation.WorkFinished(waitingBackendPid, blockingBackendPid);
                }

                await Task.Delay(PollInterval, cancellationToken);
            }

            // Re-checked after the loop, not only inside it: work that finished during the final delay is
            // still work that finished without blocking, and reporting it as a timeout would state as fact
            // that it was "still running" — pointing at a slow or misdirected wait rather than at the
            // sequencing mistake that actually happened.
            if (inFlightWork.IsCompleted)
            {
                return BackendBlockObservation.WorkFinished(waitingBackendPid, blockingBackendPid);
            }

            return BackendBlockObservation.TimedOut(
                waitingBackendPid,
                blockingBackendPid,
                timeout,
                await DescribeBackendLivenessAsync(observer, waitingBackendPid, blockingBackendPid, timeout, cancellationToken));
        }

        /// <summary>
        /// Returns the row count a <c>COUNT(*)</c> over <c>pg_stat_activity</c> produced, refusing anything
        /// that is not the bigint it yields. Named for the refusal rather than for the answer, since the
        /// throw is the part worth noticing at the call site, and for ROWS rather than for blocking —
        /// the blocked check and the two liveness checks all come through here.
        /// <para>
        /// Internal rather than private so the refusal can be pinned by a test. The query is a constant, so
        /// an unexpected scalar cannot be provoked through the public method — and an untested throw path
        /// is exactly where a silent <c>return false</c> gets reintroduced, which is the failure this
        /// refusal exists to prevent.
        /// </para>
        /// </summary>
        internal static long RequireRowCount(object? scalar)
        {
            if (scalar is not long count)
            {
                throw new InvalidOperationException(
                    $"Expected a bigint count from pg_stat_activity, got {scalar?.GetType().Name ?? "null"}.");
            }

            return count;
        }

        private static async Task<bool> IsBlockedByAsync(
            NpgsqlConnection observer,
            int waitingBackendPid,
            int blockingBackendPid,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            await using NpgsqlCommand command = new NpgsqlCommand(IsBlockedByQuery, observer);
            command.Parameters.AddWithValue("waitingPid", waitingBackendPid);
            command.Parameters.AddWithValue("blockingPid", blockingBackendPid);
            command.CommandTimeout = PollCommandTimeoutSeconds(timeout);

            return RequireRowCount(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        }

        /// <summary>
        /// Reports whether each pid is still a live backend, appended to the timed-out description. A pid
        /// that has gone — a connection closed early, or one that was never open — produces exactly the
        /// same "not blocked" reading as a backend that simply never waited, and the caller would otherwise
        /// be told the interleaving was not reached with no hint that the question itself was malformed.
        /// </summary>
        private static async Task<string> DescribeBackendLivenessAsync(
            NpgsqlConnection observer,
            int waitingBackendPid,
            int blockingBackendPid,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            try
            {
                await using NpgsqlCommand command = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM pg_stat_activity WHERE pid = @pid", observer);
                NpgsqlParameter pid = command.Parameters.AddWithValue("pid", waitingBackendPid);
                command.CommandTimeout = PollCommandTimeoutSeconds(timeout);

                bool waitingIsLive = RequireRowCount(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                pid.Value = blockingBackendPid;
                bool blockingIsLive = RequireRowCount(await command.ExecuteScalarAsync(cancellationToken)) > 0;

                if (waitingIsLive && blockingIsLive)
                {
                    return "Both backends were still live, so the pids are at least the right shape.";
                }

                // Each branch carries its own verb. Splitting the sentence into a subject and a shared
                // "was live" tail reads correctly only for the both-dead case, where "neither A nor B"
                // supplies the negation — which is exactly how an inverted claim about the waiting backend
                // shipped green once already.
                string missing = (waitingIsLive, blockingIsLive) switch
                {
                    (false, true) => $"backend {waitingBackendPid} (the waiting one) was no longer a live backend",
                    (true, false) => $"backend {blockingBackendPid} (the blocking one) was no longer a live backend",
                    _ => $"neither backend {waitingBackendPid} nor backend {blockingBackendPid} was a live backend",
                };

                return $"Note that {missing} at that point, so the pids may be stale or swapped.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Best-effort detail only. Letting this throw would replace a diagnosable "never reached"
                // report with an exception about the observer's own connection — burying the finding, which
                // is the exact failure returning an observation rather than throwing exists to prevent.
                return $"The liveness of the two backends could not be read ({exception.GetType().Name}).";
            }
        }

        /// <summary>
        /// Bounds a single poll by the caller's own budget instead of leaving it at Npgsql's 30 second
        /// default, which one stalled poll would otherwise burn — a hundred times over, for a sub-second
        /// budget — while the caller believes it set a limit.
        /// <para>
        /// Rounded UP, which is the whole subtlety. <see cref="NpgsqlCommand.CommandTimeout"/> is whole
        /// seconds and reads zero as NO limit, so truncating any sub-second budget would silently restore
        /// the unbounded poll this exists to remove. Rounding up needs no floor beside it: the budget is
        /// already guaranteed positive by <see cref="WaitForBlockAsync"/>, and the ceiling of any positive
        /// value is at least one.
        /// </para>
        /// </summary>
        internal static int PollCommandTimeoutSeconds(TimeSpan timeout)
        {
            return (int)Math.Ceiling(timeout.TotalSeconds);
        }
    }
}
