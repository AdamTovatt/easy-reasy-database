using EasyReasy.EnvironmentVariables;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Environment variables required for database mapping integration tests.
    /// </summary>
    [EnvironmentVariableNameContainer]
    public static class EnvironmentVariables
    {
        /// <summary>
        /// PostgreSQL database connection string for integration tests.
        /// </summary>
        [EnvironmentVariableName(minLength: 10)]
        public static readonly VariableName DatabaseConnectionString = new VariableName("DATABASE_CONNECTION_STRING");
    }
}
