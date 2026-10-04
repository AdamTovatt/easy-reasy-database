using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace EasyReasy.Database.Tests.TestDoubles
{
    /// <summary>
    /// Connection that opens without a database, records whether it was disposed,
    /// and fails every attempt to begin a transaction.
    /// </summary>
    internal sealed class TrackingDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotSupportedException();
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new InvalidOperationException("Beginning a transaction failed");
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            _state = ConnectionState.Closed;
            base.Dispose(disposing);
        }
    }
}
