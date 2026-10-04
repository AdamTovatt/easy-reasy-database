using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Represents a database session that always has an active transaction.
    /// A repository method that must run inside a transaction takes this type as a required parameter,
    /// so passing a session without a transaction fails at compile time instead of at runtime.
    /// </summary>
    public interface IDbTransactionSession : IDbSession
    {
        /// <summary>
        /// Gets the active transaction. Never null.
        /// </summary>
        /// <remarks>
        /// This member hides <see cref="IDbSession.Transaction"/>, so the two are separate interface members.
        /// An implementation must return the same transaction from both. A class with one public
        /// <c>Transaction</c> property does that automatically; a mock has to set up both members.
        /// </remarks>
        new DbTransaction Transaction { get; }
    }
}
