using System.Data.Common;

namespace EasyReasy.Database.Testing
{
    /// <summary>
    /// Provides database reset and setup operations for test database management.
    /// </summary>
    public interface ITestDatabaseSetup
    {
        /// <summary>
        /// Resets the database to a clean state (e.g., drops and recreates schema).
        /// Note: This operation cannot be performed within a transaction.
        /// </summary>
        /// <param name="connection">The database connection to use for reset operations.</param>
        Task ResetDatabaseAsync(DbConnection connection);

        /// <summary>
        /// Sets up the database (e.g., runs migrations, seed data).
        /// </summary>
        void SetupDatabase();
    }
}

