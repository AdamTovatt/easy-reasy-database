using System.Data;
using System.Data.Common;

namespace EasyReasy.Database.Testing
{
    /// <summary>
    /// Do-nothing DbTransaction for unit testing services.
    /// Gives fake and mocked sessions a non-null transaction to return from
    /// <see cref="IDbTransactionSession.Transaction"/> without a real database.
    /// Commit and rollback do nothing; track them on the session instead (see <see cref="FakeDbSession"/>).
    /// </summary>
    public sealed class FakeDbTransaction : DbTransaction
    {
        /// <summary>
        /// Gets the isolation level. Always <see cref="IsolationLevel.Unspecified"/>.
        /// </summary>
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;

        /// <summary>
        /// Gets the connection. Always null, since mocked repositories don't use it.
        /// </summary>
        protected override DbConnection? DbConnection => null;

        /// <summary>
        /// Does nothing.
        /// </summary>
        public override void Commit()
        {
        }

        /// <summary>
        /// Does nothing.
        /// </summary>
        public override void Rollback()
        {
        }
    }
}
