namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// A cluster role a test harness ensures exists before its schema setup or migrations run.
    /// </summary>
    /// <param name="Name">The role name, which is interpolated into DDL and so must be a plain identifier.</param>
    /// <param name="Options">
    /// The <c>CREATE ROLE</c> options, e.g. <c>NOLOGIN</c> or <c>LOGIN PASSWORD 'dev'</c>. Unlike
    /// <paramref name="Name"/> this is RAW SQL, passed through into the statement verbatim and
    /// deliberately not validated — the option list is an open grammar with quoted literals in it, which
    /// no identifier check could accept. It is a value the harness author writes in the fixture, never
    /// one that arrives from outside the test suite; treat it as code, not as input.
    /// </param>
    /// <param name="GrantToMigrationRole">
    /// Whether the migrating role is granted membership in this role. Needed when migrations transfer
    /// object ownership to it, which PostgreSQL only allows to a member.
    /// </param>
    public sealed record TestClusterRole(string Name, string Options, bool GrantToMigrationRole = false);
}
