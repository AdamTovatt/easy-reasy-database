using System.Security.Cryptography;
using System.Text;
using EasyReasy.Database.Sql;

namespace EasyReasy.Database.Testing
{
    /// <summary>
    /// Resolves the test database a checkout owns, and the marker that records which checkout a derived
    /// database belongs to.
    /// <para>
    /// A test suite that drops and recreates its schema on every run destroys the schema of any other
    /// checkout pointed at the same database mid-run — and the failure mode is nondeterministic
    /// (<c>cache lookup failed for type</c> from desynchronised enum OIDs, spurious cast errors, wildly
    /// different pass counts between identical runs), which makes it read as flakiness rather than as
    /// collision. Deriving the database name from the checkout path gives every worktree its own
    /// database, so concurrent runs need no coordination.
    /// </para>
    /// <para>
    /// The derivation is deliberately simple enough to mirror in another language (a SHA-256 over the
    /// UTF-8 bytes of the canonical checkout path — absolute, with any trailing directory separator
    /// removed — rendered as LOWERCASE hex and truncated to <see cref="HashLength"/> characters), so a second test
    /// harness in the same repository — a Playwright suite, for example — can land on the same database
    /// by construction. That agreement is a convenience, not a correctness requirement: two harnesses
    /// that canonicalised the checkout path differently would simply own two databases instead of one,
    /// and would still be isolated from every other checkout.
    /// </para>
    /// </summary>
    public sealed class CheckoutDatabaseIdentity
    {
        /// <summary>
        /// Hash characters kept. Eight is enough that two checkouts on one machine colliding is
        /// implausible, and short enough to keep the database name readable.
        /// </summary>
        public const int HashLength = 8;

        /// <summary>
        /// Name prefix every DERIVED test database shares, e.g. <c>myproject_test_</c>. A pinned name is
        /// used verbatim and need not carry it.
        /// </summary>
        public required string DatabasePrefix { get; init; }

        /// <summary>
        /// Environment variable that pins the database name, bypassing the per-checkout derivation.
        /// CI typically sets it (one checkout per runner, and the workflow provisions a database by
        /// name), and it is the escape hatch for pointing a local run at a specific database.
        /// </summary>
        public required string PinEnvironmentVariableName { get; init; }

        /// <summary>
        /// Prefix of the ownership marker written on a database the harness derived, recording the
        /// checkout it belongs to, e.g. <c>myproject-test-checkout:</c>. A pruning sweep reads it to tell
        /// an abandoned worktree's database apart from one that is still in use.
        /// </summary>
        public required string CheckoutCommentPrefix { get; init; }

        /// <summary>
        /// The file whose presence marks the repository root, e.g. <c>MyProject.sln</c>. The root is the
        /// path the derivation keys off.
        /// </summary>
        public required string RepositoryRootFileName { get; init; }

        /// <summary>
        /// Whether this run's database name was PINNED rather than derived. A pinned name belongs to
        /// whoever chose it — CI's fixed database, or a hand-made database named after an issue — so the
        /// harness must not stamp it with an ownership marker that would enrol it in the pruning sweep.
        /// Deliberately keyed on pinned-ness rather than on the name's shape: a pin that happens to look
        /// derived is still someone else's database.
        /// </summary>
        public bool IsPinned()
        {
            return IsPinned(Environment.GetEnvironmentVariable(PinEnvironmentVariableName));
        }

        /// <inheritdoc cref="IsPinned()"/>
        /// <param name="pinnedName">The pin value to judge, as the environment would have supplied it.</param>
        public static bool IsPinned(string? pinnedName)
        {
            return !string.IsNullOrWhiteSpace(pinnedName);
        }

        /// <summary>
        /// Resolves the database name for the current run: the <see cref="PinEnvironmentVariableName"/>
        /// value if set, otherwise the name derived from the checkout this test assembly was built in.
        /// </summary>
        public string ResolveDatabaseName()
        {
            return ResolveDatabaseName(Environment.GetEnvironmentVariable(PinEnvironmentVariableName), AppContext.BaseDirectory);
        }

        /// <summary>
        /// The resolution itself, taking its inputs explicitly so it can be exercised without mutating the
        /// process environment a running fixture reads. The result is validated here, at the one place
        /// every caller goes through, rather than at each statement that interpolates it.
        /// <para>
        /// The two branches fail differently on purpose. A bad PIN is fixed by changing the environment
        /// variable, so the message names it; a bad DERIVED name can only come from
        /// <see cref="DatabasePrefix"/>, since the hash contributes hex characters only, so naming the pin
        /// there would point at the wrong knob.
        /// </para>
        /// </summary>
        public string ResolveDatabaseName(string? pinnedName, string startDirectory)
        {
            if (IsPinned(pinnedName))
            {
                string pinned = pinnedName!.Trim();

                if (!SqlIdentifier.IsPlain(pinned))
                {
                    throw new InvalidOperationException(
                        $"Test database name '{pinned}' is not a plain SQL identifier. A database name cannot be " +
                        $"parameterised, so {PinEnvironmentVariableName} must be a plain identifier.");
                }

                return pinned;
            }

            string derived = ForCheckout(FindRepositoryRoot(startDirectory));

            if (!SqlIdentifier.IsPlain(derived))
            {
                throw new InvalidOperationException(
                    $"Derived test database name '{derived}' is not a plain SQL identifier. A database name " +
                    $"cannot be parameterised, so {nameof(DatabasePrefix)} must be a plain identifier prefix " +
                    $"leaving room for the {HashLength} hash characters appended to it.");
            }

            return derived;
        }

        /// <summary>Derives the database name a checkout at <paramref name="repositoryRoot"/> owns.</summary>
        public string ForCheckout(string repositoryRoot)
        {
            return DatabasePrefix + CheckoutHash(repositoryRoot);
        }

        /// <summary>The comment stamped on a derived database to record the checkout that owns it.</summary>
        public string MarkerFor(string repositoryRoot)
        {
            return CheckoutCommentPrefix + repositoryRoot;
        }

        /// <summary>
        /// Hashes the canonical checkout path down to the hex characters that identify it: SHA-256 over
        /// the UTF-8 bytes of the absolute path with any trailing directory separator removed, rendered
        /// as LOWERCASE hex and truncated to <see cref="HashLength"/> characters. The length keeps the
        /// database name short and readable while making an accidental collision between two checkouts
        /// on one machine implausible.
        /// <para>
        /// The case matters to a mirror: a pruning sweep only recognises a name whose hash is lowercase,
        /// so a mirror emitting uppercase hex would create databases no sweep can ever reclaim.
        /// </para>
        /// </summary>
        public static string CheckoutHash(string repositoryRoot)
        {
            string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return Convert.ToHexStringLower(digest).Substring(0, HashLength);
        }

        /// <summary>The checkout this test assembly was built in.</summary>
        public string CurrentCheckoutRoot()
        {
            return FindRepositoryRoot(AppContext.BaseDirectory);
        }

        /// <summary>
        /// The checkout root above <paramref name="startDirectory"/>, which is what the derivation keys
        /// off. Wraps <see cref="RepositoryRoot.Find"/> to name the escape hatch in the failure message.
        /// </summary>
        public string FindRepositoryRoot(string startDirectory)
        {
            try
            {
                return RepositoryRoot.Find(startDirectory, RepositoryRootFileName);
            }
            catch (DirectoryNotFoundException exception)
            {
                throw new InvalidOperationException(
                    $"{exception.Message} The per-checkout test database name cannot be derived; " +
                    $"set {PinEnvironmentVariableName} to name the database explicitly.",
                    exception);
            }
        }
    }
}
