namespace EasyReasy.Database.Testing
{
    /// <summary>
    /// Fake implementation of IDbSessionFactory for unit testing services.
    /// Returns FakeDbSession instances that track method calls for verification in tests.
    /// </summary>
    public class FakeDbSessionFactory : IDbSessionFactory
    {
        private readonly FakeDbSession _session;

        /// <summary>
        /// Gets the fake session that will be returned by this factory.
        /// Use this to verify that CommitAsync, RollbackAsync, etc. were called.
        /// </summary>
        public FakeDbSession Session => _session;

        /// <summary>
        /// Gets the number of times CreateSessionAsync was called.
        /// </summary>
        public int CreateSessionCallCount { get; private set; }

        /// <summary>
        /// Gets the number of times CreateSessionWithTransactionAsync was called.
        /// </summary>
        public int CreateSessionWithTransactionCallCount { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeDbSessionFactory"/> class.
        /// </summary>
        public FakeDbSessionFactory()
        {
            _session = new FakeDbSession();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeDbSessionFactory"/> class with a custom session.
        /// </summary>
        /// <param name="session">The fake session to return.</param>
        public FakeDbSessionFactory(FakeDbSession session)
        {
            _session = session;
        }

        /// <inheritdoc/>
        public Task<IDbSession> CreateSessionAsync()
        {
            CreateSessionCallCount++;
            return Task.FromResult<IDbSession>(_session);
        }

        /// <inheritdoc/>
        public Task<IDbSession> CreateSessionWithTransactionAsync()
        {
            CreateSessionWithTransactionCallCount++;
            return Task.FromResult<IDbSession>(_session);
        }

        /// <summary>
        /// Resets all call counts and the session state.
        /// </summary>
        public void Reset()
        {
            CreateSessionCallCount = 0;
            CreateSessionWithTransactionCallCount = 0;
            _session.Reset();
        }
    }
}

