using System.Data;
using System.Data.Common;

namespace EasyReasy.Database.Tests.TestDoubles
{
    /// <summary>
    /// Transaction whose disposal throws.
    /// </summary>
    internal sealed class DisposeFailingDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;

        protected override DbConnection? DbConnection => null;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }

        protected override void Dispose(bool disposing)
        {
            throw new InvalidOperationException("Disposing the transaction failed");
        }
    }
}
