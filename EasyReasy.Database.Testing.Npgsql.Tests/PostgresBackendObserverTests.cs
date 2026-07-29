using System.Diagnostics;
using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// Guards the guard. <see cref="PostgresBackendObserver"/> is what stops a consumer's concurrency
    /// tests from passing without reaching their interleaving, and it has no consumers here — so unlike in
    /// a suite that uses it, nothing else in this repository would notice if it broke. An observer that
    /// answered "blocked" indiscriminately, or one whose predicate could never be true, would silently
    /// disarm every test resting on it while leaving all of them green.
    /// <para>
    /// Hence a positive control that makes two backends genuinely contend, and negative cases either side
    /// of it. Why each half of the predicate is needed is argued where the predicate is, on
    /// <see cref="PostgresBackendObserver"/>; what belongs here is which test fails if a half goes missing,
    /// since neither edit breaks anything else in the suite:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// Drop the <c>pg_blocking_pids</c> half and the observer reports a block immediately, always. Caught by
    /// <see cref="WaitForBlockAsync_WhenNothingIsBlockedAndTheWorkHasFinished_ReportsThatTheInterleavingWasNeverReached"/>.
    /// </item>
    /// <item>
    /// Drop the <c>pid = @waitingPid</c> half and it reports a block for any backend while some other one
    /// is waiting. Caught by the second observation in
    /// <see cref="WaitForBlockAsync_WhenOneBackendIsParkedOnAnothersTransaction_ObservesTheBlockForThatBackendOnly"/>.
    /// </item>
    /// </list>
    /// </summary>
    public class PostgresBackendObserverTests
    {
        /// <summary>
        /// The positive control: two backends genuinely contending over one row, with the observer seen
        /// returning the success case. Without it a predicate that could never be true would ship green,
        /// since every other case here expects a non-block.
        /// <para>
        /// The same fixture is then asked a second question — is the observer's own uninvolved backend
        /// blocked by the holder? — because the answer to that is what makes the first answer specific.
        /// The holder IS blocking someone at that moment, so an observer that only asked "is anything
        /// blocked by the holder" would say yes about a backend that never waited for anything.
        /// </para>
        /// <para>
        /// Row contention under a held-open transaction rather than, say, two advisory-lock waiters:
        /// contending transactions are the shape the consuming tests actually have, and a transactionid
        /// wait is what <c>pg_blocking_pids</c> has to look through to name the holder.
        /// </para>
        /// </summary>
        [Fact]
        public async Task WaitForBlockAsync_WhenOneBackendIsParkedOnAnothersTransaction_ObservesTheBlockForThatBackendOnly()
        {
            string databaseName = ThrowawayDatabaseName();
            string connectionString = TestCluster.ConnectionStringFor(databaseName);

            try
            {
                await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(connectionString, ownershipMarker: null);

                await using NpgsqlConnection holder = await OpenUnpooledAsync(connectionString);
                await using NpgsqlConnection waiter = await OpenUnpooledAsync(connectionString);

                // Deliberately a DIFFERENT database from the contending pair, which is what pins the
                // documented contract that any database in the cluster will do — pids and pg_stat_activity
                // are cluster-wide. A per-database filter added to the predicate later would fail here and
                // nowhere else.
                await using NpgsqlConnection observer = await OpenUnpooledAsync(
                    PostgresConnectionStrings.Maintenance(TestConnection.ConnectionString));

                await ExecuteAsync(holder, "CREATE TABLE contended (id integer PRIMARY KEY, value integer)");
                await ExecuteAsync(holder, "INSERT INTO contended VALUES (1, 0)");

                BackendBlockObservation contended;
                BackendBlockObservation uninvolved;

                await using (NpgsqlTransaction held = await holder.BeginTransactionAsync())
                {
                    await ExecuteAsync(holder, "UPDATE contended SET value = 1 WHERE id = 1", held);

                    // Left running deliberately: it parks on the row the held transaction has, and stays
                    // parked until that transaction ends below.
                    Task parked = ExecuteAsync(waiter, "UPDATE contended SET value = 2 WHERE id = 1");

                    try
                    {
                        contended = await PostgresBackendObserver.WaitForBlockAsync(
                            observer, waiter.ProcessID, holder.ProcessID, parked, TimeSpan.FromSeconds(10));

                        uninvolved = await PostgresBackendObserver.WaitForBlockAsync(
                            observer, observer.ProcessID, holder.ProcessID, Task.CompletedTask, TimeSpan.FromSeconds(10));
                    }
                    finally
                    {
                        // Release the contention and drain the parked statement whatever happened — the
                        // same order a consuming test has to use, and the reason the observer reports
                        // rather than throws. In a finally rather than after the awaits so that an
                        // observation that throws does not leave a command in flight against a connection
                        // being disposed, which fails in its own nondeterministic way.
                        await held.RollbackAsync();
                        await parked;
                    }
                }

                Assert.Equal(BackendBlockOutcome.Blocked, contended.Outcome);
                Assert.True(contended.BlockObserved, contended.Description);
                Assert.Contains(
                    $"Backend {waiter.ProcessID} blocked on backend {holder.ProcessID}",
                    contended.Description,
                    StringComparison.Ordinal);

                Assert.False(
                    uninvolved.BlockObserved,
                    "a backend that never waited must not be reported as blocked just because the blocking "
                    + $"backend is holding someone else up: {uninvolved.Description}");
            }
            finally
            {
                await TestCluster.DropDatabaseAsync(databaseName);
            }
        }

        /// <summary>
        /// Two live backends, neither blocking the other, and work that has already finished — so the
        /// observer must report a non-block. An observer that ignores <c>pg_blocking_pids</c> reports a
        /// block here, which is exactly the failure mode a consuming race test cannot detect for itself.
        /// </summary>
        [Fact]
        public async Task WaitForBlockAsync_WhenNothingIsBlockedAndTheWorkHasFinished_ReportsThatTheInterleavingWasNeverReached()
        {
            await using NpgsqlConnection observer = await OpenUnpooledAsync(TestConnection.ConnectionString);
            await using NpgsqlConnection idle = await OpenUnpooledAsync(TestConnection.ConnectionString);

            BackendBlockObservation observation = await PostgresBackendObserver.WaitForBlockAsync(
                observer, idle.ProcessID, observer.ProcessID, Task.CompletedTask, TimeSpan.FromSeconds(10));

            Assert.False(observation.BlockObserved, observation.Description);
            Assert.Equal(BackendBlockOutcome.WorkFinishedWithoutBlocking, observation.Outcome);

            // "finished without ever blocking", not "never reached": both non-block messages end with the
            // latter, so asserting on it would add nothing to the Assert.Equal above.
            Assert.Contains("finished without ever blocking", observation.Description, StringComparison.Ordinal);
        }

        /// <summary>
        /// The other exit: work that never finishes and never blocks. The observer must not mistake a
        /// long-running statement for a blocked one, and must give up rather than hang.
        /// <para>
        /// The elapsed time is asserted, not just the outcome. Without it a poll left at Npgsql's 30
        /// second default command timeout could stall for a hundred times the stated budget and still
        /// arrive here green, which would make the timeout a suggestion rather than a bound.
        /// </para>
        /// </summary>
        [Fact]
        public async Task WaitForBlockAsync_WhenTheWorkIsStillRunningAndUnblocked_GivesUpAtTheTimeout()
        {
            await using NpgsqlConnection observer = await OpenUnpooledAsync(TestConnection.ConnectionString);
            await using NpgsqlConnection idle = await OpenUnpooledAsync(TestConnection.ConnectionString);

            using CancellationTokenSource neverCompletes = new CancellationTokenSource();
            Task pending = Task.Delay(Timeout.Infinite, neverCompletes.Token);

            TimeSpan budget = TimeSpan.FromMilliseconds(300);
            long start = Stopwatch.GetTimestamp();

            BackendBlockObservation observation = await PostgresBackendObserver.WaitForBlockAsync(
                observer, idle.ProcessID, observer.ProcessID, pending, budget);

            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            await neverCompletes.CancelAsync();

            Assert.Equal(BackendBlockOutcome.TimedOut, observation.Outcome);
            Assert.Contains("never blocked", observation.Description, StringComparison.Ordinal);

            // Generous, because it has to survive a loaded CI runner: this is bounding the runaway case,
            // not measuring the poll interval.
            Assert.True(
                elapsed < TimeSpan.FromSeconds(5),
                $"the wait must be bounded by its {budget.TotalMilliseconds} ms budget, but took {elapsed.TotalMilliseconds:0} ms");
        }

        /// <summary>
        /// The in-flight work is checked once more AFTER the loop, not only inside it. Work that finishes
        /// during the final poll delay has still finished without blocking, and calling that a timeout
        /// would state as fact that it was "still running" — sending the reader after a slow or misdirected
        /// wait instead of the sequencing mistake that actually happened.
        /// <para>
        /// The timing: the budget is shorter than one poll interval, so exactly one iteration runs. The
        /// work is completed while that iteration sits in its delay, which is after the in-loop check and
        /// before the post-loop one. If a loaded machine were slow enough that the in-loop check caught it
        /// instead, this test would still pass — the outcome asserted is the same either way — so the
        /// margin buys specificity, not stability.
        /// </para>
        /// </summary>
        [Fact]
        public async Task WaitForBlockAsync_WhenTheWorkFinishesDuringTheFinalPollDelay_StillReportsThatItFinished()
        {
            await using NpgsqlConnection observer = await OpenUnpooledAsync(TestConnection.ConnectionString);
            await using NpgsqlConnection idle = await OpenUnpooledAsync(TestConnection.ConnectionString);

            TaskCompletionSource work = new TaskCompletionSource();

            Task<BackendBlockObservation> observing = PostgresBackendObserver.WaitForBlockAsync(
                observer, idle.ProcessID, observer.ProcessID, work.Task, TimeSpan.FromMilliseconds(1));

            await Task.Delay(TimeSpan.FromMilliseconds(15));
            work.SetResult();

            BackendBlockObservation observation = await observing;

            Assert.Equal(BackendBlockOutcome.WorkFinishedWithoutBlocking, observation.Outcome);
            Assert.Contains("finished without ever blocking", observation.Description, StringComparison.Ordinal);
        }

        /// <summary>
        /// A pid whose backend has gone reads exactly like a backend that never waited, so the timed-out
        /// description has to say which it was. Without this the caller is told the interleaving was not
        /// reached, with nothing to suggest the question itself was malformed.
        /// <para>
        /// All three branches, and each asserted as ONE contiguous clause including its verb. The first
        /// version of this test asserted the subject and the trailing advice as two separate substrings,
        /// which straddled the verb — and an inverted "was live" for a backend that was not live passed it
        /// green. A negation is exactly the kind of claim that split assertions cannot see.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task WaitForBlockAsync_WhenAPidIsNoLongerALiveBackend_SaysWhichOneInTheDescription(
            bool waitingHasDeparted, bool blockingHasDeparted)
        {
            await using NpgsqlConnection observer = await OpenUnpooledAsync(TestConnection.ConnectionString);
            await using NpgsqlConnection live = await OpenUnpooledAsync(TestConnection.ConnectionString);

            int waitingPid = await PidOfADepartedBackendOrAsync(waitingHasDeparted, live);
            int blockingPid = await PidOfADepartedBackendOrAsync(blockingHasDeparted, observer);

            using CancellationTokenSource neverCompletes = new CancellationTokenSource();
            Task pending = Task.Delay(Timeout.Infinite, neverCompletes.Token);

            BackendBlockObservation observation = await PostgresBackendObserver.WaitForBlockAsync(
                observer, waitingPid, blockingPid, pending, TimeSpan.FromMilliseconds(300));

            await neverCompletes.CancelAsync();

            string expected = (waitingHasDeparted, blockingHasDeparted) switch
            {
                (true, false) => $"backend {waitingPid} (the waiting one) was no longer a live backend at that point",
                (false, true) => $"backend {blockingPid} (the blocking one) was no longer a live backend at that point",
                _ => $"neither backend {waitingPid} nor backend {blockingPid} was a live backend at that point",
            };

            Assert.Equal(BackendBlockOutcome.TimedOut, observation.Outcome);
            Assert.Contains(expected, observation.Description, StringComparison.Ordinal);
        }

        /// <summary>
        /// Both pids live, which is the branch that must NOT accuse them. Separate from the theory above
        /// because it is the opposite claim, not a fourth case of the same one.
        /// </summary>
        [Fact]
        public async Task WaitForBlockAsync_WhenBothPidsAreLive_SaysTheyWereRatherThanAccusingThem()
        {
            await using NpgsqlConnection observer = await OpenUnpooledAsync(TestConnection.ConnectionString);
            await using NpgsqlConnection live = await OpenUnpooledAsync(TestConnection.ConnectionString);

            using CancellationTokenSource neverCompletes = new CancellationTokenSource();
            Task pending = Task.Delay(Timeout.Infinite, neverCompletes.Token);

            BackendBlockObservation observation = await PostgresBackendObserver.WaitForBlockAsync(
                observer, live.ProcessID, observer.ProcessID, pending, TimeSpan.FromMilliseconds(300));

            await neverCompletes.CancelAsync();

            Assert.Contains("Both backends were still live", observation.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("no longer a live backend", observation.Description, StringComparison.Ordinal);
        }

        /// <summary>
        /// The pid of a backend that has gone, or of one that is still there — the two inputs the liveness
        /// report has to tell apart. Closing an UNPOOLED connection really ends its session, which is what
        /// makes the departed case observable rather than merely returned to a pool.
        /// </summary>
        private static async Task<int> PidOfADepartedBackendOrAsync(bool departed, NpgsqlConnection stillOpen)
        {
            if (!departed)
            {
                return stillOpen.ProcessID;
            }

            NpgsqlConnection departing = await OpenUnpooledAsync(TestConnection.ConnectionString);
            int pid = departing.ProcessID;
            await departing.DisposeAsync();
            return pid;
        }

        /// <summary>
        /// The per-poll bound, pinned where it is observable. Rounding is the load-bearing part: Npgsql
        /// reads a <c>CommandTimeout</c> of zero as NO limit, so truncating a sub-second budget would
        /// restore the unbounded poll this derivation exists to remove — turning the tightest budget a
        /// caller can ask for into the loosest one there is.
        /// <para>
        /// One second is therefore the floor in practice, and every sub-second budget gets it. That is
        /// looser than such a caller asked for, but the failure it permits is a loud Npgsql timeout rather
        /// than a wrong answer, and a second is a wide margin for a single catalog read.
        /// </para>
        /// </summary>
        [Fact]
        public void PollCommandTimeoutSeconds_ForASubSecondBudget_RoundsUpRatherThanToNoLimitAtAll()
        {
            Assert.Equal(1, PostgresBackendObserver.PollCommandTimeoutSeconds(TimeSpan.FromMilliseconds(300)));
            Assert.Equal(1, PostgresBackendObserver.PollCommandTimeoutSeconds(TimeSpan.FromMilliseconds(1)));
            Assert.Equal(11, PostgresBackendObserver.PollCommandTimeoutSeconds(TimeSpan.FromSeconds(10.2)));
            Assert.Equal(10, PostgresBackendObserver.PollCommandTimeoutSeconds(TimeSpan.FromSeconds(10)));
        }

        /// <summary>
        /// A budget too small to fit a poll must be refused rather than answered. Returning an observation
        /// there would report that the interleaving was never reached without a single query having run —
        /// indistinguishable, to the caller, from a real non-block.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task WaitForBlockAsync_WithANonPositiveTimeout_ThrowsRatherThanReportingANonBlock(int milliseconds)
        {
            await using NpgsqlConnection observer = await OpenUnpooledAsync(TestConnection.ConnectionString);

            ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                PostgresBackendObserver.WaitForBlockAsync(
                    observer, observer.ProcessID, observer.ProcessID, Task.CompletedTask,
                    TimeSpan.FromMilliseconds(milliseconds)));

            Assert.Equal("timeout", exception.ParamName);
        }

        /// <summary>
        /// The guards are the file's own argument applied to itself: an untested throw is where a silent
        /// fallback gets reintroduced. A null observer would otherwise surface as a NullReferenceException
        /// from inside the poll, naming nothing the caller can act on.
        /// </summary>
        [Fact]
        public async Task WaitForBlockAsync_WithANullArgument_NamesTheArgument()
        {
            await using NpgsqlConnection observer = await OpenUnpooledAsync(TestConnection.ConnectionString);

            ArgumentNullException nullObserver = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PostgresBackendObserver.WaitForBlockAsync(
                    null!, 1, 2, Task.CompletedTask, TimeSpan.FromSeconds(1)));
            Assert.Equal("observer", nullObserver.ParamName);

            ArgumentNullException nullWork = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PostgresBackendObserver.WaitForBlockAsync(
                    observer, 1, 2, null!, TimeSpan.FromSeconds(1)));
            Assert.Equal("inFlightWork", nullWork.ParamName);
        }

        /// <summary>
        /// The token has to reach the loop, not merely be accepted. The timeout here is far longer than the
        /// cancellation, so an implementation that took the token and ignored it would run for the full
        /// thirty times as long and then return an observation instead of throwing.
        /// </summary>
        [Fact]
        public async Task WaitForBlockAsync_WhenTheTokenIsCancelledMidWait_AbandonsTheWait()
        {
            await using NpgsqlConnection observer = await OpenUnpooledAsync(TestConnection.ConnectionString);
            await using NpgsqlConnection idle = await OpenUnpooledAsync(TestConnection.ConnectionString);

            using CancellationTokenSource abandoned = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            using CancellationTokenSource neverCompletes = new CancellationTokenSource();
            Task pending = Task.Delay(Timeout.Infinite, neverCompletes.Token);

            long start = Stopwatch.GetTimestamp();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PostgresBackendObserver.WaitForBlockAsync(
                    observer, idle.ProcessID, observer.ProcessID, pending, TimeSpan.FromSeconds(10), abandoned.Token));

            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            await neverCompletes.CancelAsync();

            // The throw alone would not prove the doc's claim: an implementation that ignored the token
            // throughout and merely called ThrowIfCancellationRequested at the very end would satisfy it,
            // after running the full ten seconds. Cancelling has to actually cut the wait short.
            Assert.True(
                elapsed < TimeSpan.FromSeconds(5),
                $"cancelling must abandon the wait rather than run out its budget, but it took {elapsed.TotalMilliseconds:0} ms");
        }

        /// <summary>
        /// The refusal that keeps a wrong diagnosis from being confident. A silent <c>false</c> here reads
        /// as "not blocked yet", so the caller burns its whole timeout and then reports that the
        /// interleaving was never reached — of a test that may be working perfectly.
        /// <para>
        /// Pinned on the internal seam rather than through <c>WaitForBlockAsync</c>, because the query it
        /// runs is a constant: <c>COUNT(*)</c> always yields a bigint, so no connection state can provoke
        /// this from outside. Untested, the throw is exactly the kind of path a later tidy-up turns back
        /// into a <c>return false</c>.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("1")]
        [InlineData(1)]
        [InlineData(1.0)]
        public void RequireRowCount_ForAnythingOtherThanTheBigintCount_Throws(object? scalar)
        {
            Assert.Throws<InvalidOperationException>(() => PostgresBackendObserver.RequireRowCount(scalar));
        }

        /// <summary>
        /// The canonical ADO.NET stand-in for a missing value, which an attribute argument cannot express
        /// so it cannot join the theory above. Not hypothetical: a rewrite of the predicate to an aggregate
        /// over an empty result — <c>MAX</c> rather than <c>COUNT</c> — yields exactly this, and it is the
        /// case most likely to be mistaken for a legitimate "not blocked".
        /// </summary>
        [Fact]
        public void RequireRowCount_ForDbNull_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => PostgresBackendObserver.RequireRowCount(DBNull.Value));
        }

        /// <summary>The bigint passes through untouched; the caller is what compares it to zero.</summary>
        [Fact]
        public void RequireRowCount_ForTheBigintCount_ReturnsIt()
        {
            Assert.Equal(0L, PostgresBackendObserver.RequireRowCount(0L));
            Assert.Equal(2L, PostgresBackendObserver.RequireRowCount(2L));
        }

        /// <summary>
        /// Unpooled throughout: a pooled session outlives the <see cref="NpgsqlConnection"/> that borrowed
        /// it, so a backend pid this test reasons about could still be alive — and holding the pool's idea
        /// of a clean session — after the test that produced the contention has finished.
        /// </summary>
        private static async Task<NpgsqlConnection> OpenUnpooledAsync(string connectionString)
        {
            NpgsqlConnection connection = new NpgsqlConnection(PostgresConnectionStrings.Unpooled(connectionString));
            await connection.OpenAsync();
            return connection;
        }

        private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
        {
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        private static string ThrowawayDatabaseName()
        {
            return TestCluster.ThrowawayDatabaseName("easyreasy_observer_test_");
        }
    }
}
