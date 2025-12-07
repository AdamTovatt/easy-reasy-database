using System.Data.Common;

namespace EasyReasy.Database.Testing
{
    /// <summary>
    /// Provides shared database setup and management for tests.
    /// Responsible for creating and recreating the test DbDataSource,
    /// ensuring a clean database schema, and creating transaction sessions.
    /// </summary>
    public class TestDatabaseManager
    {
        private readonly IDataSourceFactory _dataSourceFactory;
        private readonly Func<string> _connectionStringProvider;

        private readonly object _syncRoot = new object();

        private bool _isDataSourceInitialized = false;
        private bool _hasCleanedDatabase = false;
        private DbDataSource _dataSource = null!;
        private IDbSessionFactory? _sessionFactory;

        /// <summary>
        /// Gets the database data source configured for testing.
        /// </summary>
        public DbDataSource DataSource
        {
            get
            {
                InitializeDataSource();
                return _dataSource;
            }
        }

        /// <summary>
        /// Gets the session factory for creating database sessions.
        /// </summary>
        private IDbSessionFactory SessionFactory
        {
            get
            {
                InitializeDataSource();
                return _sessionFactory ??= new DbSessionFactory(_dataSource);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestDatabaseManager"/> class.
        /// </summary>
        /// <param name="dataSourceFactory">The factory to use for creating data sources.</param>
        /// <param name="connectionStringProvider">A function that returns the connection string to use.</param>
        public TestDatabaseManager(IDataSourceFactory dataSourceFactory, Func<string> connectionStringProvider)
        {
            _dataSourceFactory = dataSourceFactory ?? throw new ArgumentNullException(nameof(dataSourceFactory));
            _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        }

        /// <summary>
        /// Initializes the test database data source using the configured dependencies.
        /// Safe to call multiple times; initialization will only happen once.
        /// </summary>
        public void InitializeDataSource()
        {
            if (_isDataSourceInitialized)
                return;

            lock (_syncRoot)
            {
                if (_isDataSourceInitialized)
                    return;

                string connectionString = _connectionStringProvider();
                _dataSource = _dataSourceFactory.CreateDataSource(connectionString);

                _isDataSourceInitialized = true;
            }
        }

        /// <summary>
        /// Disposes the current database data source and creates a new one.
        /// Used after schema changes that affect type OIDs.
        /// </summary>
        public void RecreateDataSource()
        {
            lock (_syncRoot)
            {
                if (_isDataSourceInitialized)
                {
                    _dataSource.Dispose();
                    _isDataSourceInitialized = false;
                    _sessionFactory = null;
                }

                InitializeDataSource();
            }
        }

        /// <summary>
        /// Ensures the database has a clean setup by first resetting it and then running setup operations.
        /// Has a private bool state to ensure it only actually resets the database once per test run so that
        /// this function can be called at the start of all tests that use the database but only the first will actually
        /// reset the database. The rest should run their changes in transactions that are rolled back at the end of
        /// whatever they do.
        /// </summary>
        /// <param name="setup">Optional database setup implementation. If null, no reset or setup operations are performed.</param>
        public async Task EnsureCleanDatabaseSetupAsync(ITestDatabaseSetup? setup = null)
        {
            InitializeDataSource();

            // Super fast check: if we've already cleaned and setup this session, return immediately
            if (_hasCleanedDatabase)
                return;

            if (setup == null)
                return;

            // Reset the database
            await using (DbConnection connection = await _dataSource.OpenConnectionAsync())
            {
                await setup.ResetDatabaseAsync(connection);
            }

            // IMPORTANT: Recreate the data source after schema reset
            // The schema drop destroys all custom types (including enums),
            // and they get new OIDs when recreated by migrations.
            // The DataSource needs to be recreated to pick up the new OIDs.
            RecreateDataSource();

            setup.SetupDatabase();
            _hasCleanedDatabase = true;
        }

        /// <summary>
        /// Creates a new database session with an active transaction.
        /// </summary>
        /// <returns>A new IDbSession with an active transaction.</returns>
        public async Task<IDbSession> CreateTransactionSessionAsync()
        {
            InitializeDataSource();
            return await SessionFactory.CreateSessionWithTransactionAsync();
        }
    }
}

