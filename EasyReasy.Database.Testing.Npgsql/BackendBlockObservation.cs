using System.Globalization;

namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// What <see cref="PostgresBackendObserver.WaitForBlockAsync"/> saw: the outcome, and a sentence
    /// describing it that a caller can hand straight to an assertion message.
    /// <para>
    /// Returned rather than thrown, because a caller that has to fail generally has cleanup to do FIRST:
    /// a test holding a transaction open to create the contention must release it and drain the still
    /// parked work before failing, since an abandoned task throws on disposal and that secondary
    /// exception is what the test runner reports — burying the observation's own message, which is the
    /// only thing that failure has to say.
    /// </para>
    /// </summary>
    /// <param name="Outcome">Which of the three ways the wait ended.</param>
    /// <param name="Description">
    /// A sentence describing what happened, populated for every outcome so it can be passed as an
    /// assertion message without the caller first checking which outcome it got.
    /// </param>
    public sealed record BackendBlockObservation(BackendBlockOutcome Outcome, string Description)
    {
        /// <summary>
        /// Whether the interleaving was reached. The one thing most callers branch on, named so the call
        /// site reads as the question it is asking rather than as an enum comparison.
        /// </summary>
        public bool BlockObserved => Outcome == BackendBlockOutcome.Blocked;

        /// <summary>The interleaving was reached: the waiting backend was seen parked on the blocking one.</summary>
        internal static BackendBlockObservation Blocked(int waitingBackendPid, int blockingBackendPid)
        {
            return new BackendBlockObservation(
                BackendBlockOutcome.Blocked,
                $"Backend {waitingBackendPid} blocked on backend {blockingBackendPid}, so the interleaving "
                + "under test was reached.");
        }

        /// <summary>
        /// The work ran to completion without ever waiting. Reachable from two places in the poll loop —
        /// inside it and once more after it — which is why the wording lives here rather than at either.
        /// </summary>
        internal static BackendBlockObservation WorkFinished(int waitingBackendPid, int blockingBackendPid)
        {
            return new BackendBlockObservation(
                BackendBlockOutcome.WorkFinishedWithoutBlocking,
                $"Backend {waitingBackendPid} finished without ever blocking on backend "
                + $"{blockingBackendPid}, so the interleaving under test was never reached.");
        }

        /// <summary>
        /// The budget elapsed with the work still running and still unblocked.
        /// <paramref name="livenessNote"/> carries whether each pid was still a live backend, since a
        /// departed pid reads exactly like a backend that simply never waited.
        /// </summary>
        internal static BackendBlockObservation TimedOut(
            int waitingBackendPid, int blockingBackendPid, TimeSpan timeout, string livenessNote)
        {
            return new BackendBlockObservation(
                BackendBlockOutcome.TimedOut,
                $"Backend {waitingBackendPid} never blocked on backend {blockingBackendPid} within "
                + $"{timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds, so the "
                + $"interleaving under test was never reached. {livenessNote}");
        }
    }
}
