using System.Globalization;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// The shared role bootstrap, covered at two levels. The SQL SHAPE is pinned by string, because a
    /// consuming repository's non-.NET harness may create the same cluster roles with its own copy of the
    /// statement and needs something to mirror. The SQL itself is then EXECUTED against the cluster,
    /// because a string assertion passes just as happily on SQL PostgreSQL would reject.
    /// </summary>
    public class TestClusterRolesTests : IAsyncLifetime
    {
        /// <summary>
        /// Role names of this run's own. Roles are CLUSTER objects — the reason the bootstrap needs a lock
        /// at all — so fixed names would collide with a concurrent checkout's run of this same test.
        /// </summary>
        private readonly string _ownerRole = "easyreasy_roles_test_owner_" + Guid.NewGuid().ToString("n")[..8];
        private readonly string _writerRole = "easyreasy_roles_test_writer_" + Guid.NewGuid().ToString("n")[..8];
        private readonly string _migrationRole = "easyreasy_roles_test_migrator_" + Guid.NewGuid().ToString("n")[..8];

        /// <summary>Negative, keeping it clear of the positive keys this library recommends to consumers.</summary>
        private readonly long _lockKey = -(long)uint.Parse(
            Guid.NewGuid().ToString("n")[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        public async Task InitializeAsync()
        {
            // The migration role is a precondition, not part of what is under test: EnsureAsync grants
            // membership TO it, which requires it to already exist.
            await TestCluster.DropRoleAsync(_migrationRole);
            await TestCluster.CreateRoleAsync(_migrationRole, "NOLOGIN");
        }

        public async Task DisposeAsync()
        {
            // Cluster-wide, so leaving any of these behind leaks into every other checkout's run.
            await TestCluster.DropRoleAsync(_ownerRole);
            await TestCluster.DropRoleAsync(_writerRole);
            await TestCluster.DropRoleAsync(_migrationRole);
        }

        /// <summary>
        /// The bootstrap end to end: the roles exist afterwards, and the migration role is a member of
        /// exactly the one marked for it. This is the test that would catch SQL the shape assertions
        /// accept but PostgreSQL will not parse.
        /// </summary>
        [Fact]
        public async Task EnsureAsync_OnAClusterMissingTheRoles_CreatesThemAndGrantsTheMarkedOne()
        {
            await TestClusterRoles.EnsureAsync(
                TestConnection.ConnectionString,
                _migrationRole,
                [
                    new TestClusterRole(_ownerRole, "NOLOGIN", GrantToMigrationRole: true),
                    new TestClusterRole(_writerRole, "LOGIN PASSWORD 'dev'"),
                ],
                _lockKey);

            Assert.True(await TestCluster.RoleExistsAsync(_ownerRole), "the owner role must be created");
            Assert.True(await TestCluster.RoleExistsAsync(_writerRole), "the writer role must be created");
            Assert.True(
                await TestCluster.RoleIsMemberOfAsync(_migrationRole, _ownerRole),
                "migrations transfer ownership to the owner role, which PostgreSQL only allows to a member");
            Assert.False(
                await TestCluster.RoleIsMemberOfAsync(_migrationRole, _writerRole),
                "a role not marked GrantToMigrationRole must not be granted");
        }

        /// <summary>
        /// Idempotence, which is the whole point: every test run of every checkout calls this, so the
        /// second call must be a no-op rather than a failure.
        /// </summary>
        [Fact]
        public async Task EnsureAsync_RunTwiceOverTheSameRoles_SucceedsBothTimes()
        {
            IReadOnlyList<TestClusterRole> roles = [new TestClusterRole(_ownerRole, "NOLOGIN", GrantToMigrationRole: true)];

            await TestClusterRoles.EnsureAsync(TestConnection.ConnectionString, _migrationRole, roles, _lockKey);
            await TestClusterRoles.EnsureAsync(TestConnection.ConnectionString, _migrationRole, roles, _lockKey);

            Assert.True(await TestCluster.RoleExistsAsync(_ownerRole), "the role must still be there after a second run");
            Assert.True(await TestCluster.RoleIsMemberOfAsync(_migrationRole, _ownerRole), "the grant must survive a second run");
        }

        /// <summary>
        /// The race the exception handler exists for, provoked rather than described: several bootstraps
        /// starting at once, as concurrent checkouts do. The cluster lock should serialise them, so this
        /// also asserts that the lock does its job — if it did not, the loser would surface here as an
        /// unhandled <c>unique_violation</c>.
        /// </summary>
        [Fact]
        public async Task EnsureAsync_FromSeveralConcurrentBootstraps_SerialisesWithoutFailing()
        {
            IReadOnlyList<TestClusterRole> roles = [new TestClusterRole(_ownerRole, "NOLOGIN", GrantToMigrationRole: true)];

            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                TestClusterRoles.EnsureAsync(TestConnection.ConnectionString, _migrationRole, roles, _lockKey)));

            Assert.True(await TestCluster.RoleExistsAsync(_ownerRole), "exactly one bootstrap should have created the role, and none should have failed");
        }

        /// <summary>
        /// The existence check alone is not enough: two checkouts' runs can both pass it before either
        /// creates the role. Verified against PostgreSQL 16 that the loser of that race gets
        /// <c>unique_violation</c> from <c>pg_authid_rolname_index</c> — NOT <c>duplicate_object</c>, which
        /// is what the same statement raises when the role was already visible — so handling only the
        /// latter would let the real race escape.
        /// </summary>
        [Fact]
        public void CreateRoleIfMissingSql_ForAnyRole_GuardsTheRaceWithBothSqlStatesItCanRaise()
        {
            string sql = TestClusterRoles.CreateRoleIfMissingSql("example_audit_owner", "NOLOGIN");

            Assert.Contains("IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'example_audit_owner')", sql);
            Assert.Contains("CREATE ROLE example_audit_owner NOLOGIN;", sql);
            Assert.Contains("EXCEPTION WHEN duplicate_object OR unique_violation THEN", sql);
        }

        /// <summary>
        /// The options are raw SQL by design — an open grammar with quoted literals in it that no
        /// identifier check could accept — so they pass through untouched. Pinned so the pass-through
        /// stays a deliberate decision rather than an oversight someone later "fixes" by escaping it.
        /// </summary>
        [Fact]
        public void CreateRoleIfMissingSql_WithOptionsContainingALiteral_CarriesThemThroughVerbatim()
        {
            Assert.Contains(
                "CREATE ROLE example_audit_writer LOGIN PASSWORD 'dev';",
                TestClusterRoles.CreateRoleIfMissingSql("example_audit_writer", "LOGIN PASSWORD 'dev'"));
        }

        /// <summary>
        /// Role names are interpolated into DDL, so the bootstrap must refuse non-plain identifiers before
        /// anything reaches a statement — and before it even connects, which is why an unreachable
        /// connection string suffices here and proves the refusal came first.
        /// </summary>
        [Fact]
        public async Task EnsureAsync_WithANonPlainRoleName_ThrowsWithoutConnecting()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TestClusterRoles.EnsureAsync(
                    "Host=nowhere.invalid;Username=postgres;Password=none",
                    migrationRole: "postgres",
                    roles: [new TestClusterRole("bad role; DROP ROLE postgres", "NOLOGIN")],
                    lockKey: 1));
        }

        /// <inheritdoc cref="EnsureAsync_WithANonPlainRoleName_ThrowsWithoutConnecting"/>
        [Fact]
        public async Task EnsureAsync_WithANonPlainMigrationRole_ThrowsWithoutConnecting()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TestClusterRoles.EnsureAsync(
                    "Host=nowhere.invalid;Username=postgres;Password=none",
                    migrationRole: "postgres; DROP ROLE postgres",
                    roles: [new TestClusterRole("example_role", "NOLOGIN")],
                    lockKey: 1));
        }
    }
}
