using DentalLoyalty.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// A uniquely named, disposable database on the configured test SQL Server,
// migrated with the real migrations and dropped on dispose (ADR-005 Testing).
//
// Safety guards - a test must never touch any database it did not create:
//   - the configured value names a server only: a database name or
//     AttachDbFilename is rejected, and the server must be on this machine;
//   - the database name is always generated here, and must not exist yet;
//   - the created database's identity (database_id + create_date) is recorded,
//     and the database is dropped only if that exact database still exists
//     under its name - otherwise dropping is refused;
//   - a database created by a failed setup is dropped the same way.
public sealed class SqlServerTestDatabase : IAsyncDisposable
{
    public const string ServerConnectionStringVariable = "DENTALLOYALTY_TEST_SQLSERVER";

    private const string DatabaseNamePrefix = "DentalLoyalty_IntegrationTests_";

    private readonly string _masterConnectionString;
    private readonly string _databaseName;
    private readonly int _databaseId;
    private readonly DateTime _createDate;

    private SqlServerTestDatabase(
        string masterConnectionString,
        string connectionString,
        string databaseName,
        int databaseId,
        DateTime createDate)
    {
        _masterConnectionString = masterConnectionString;
        ConnectionString = connectionString;
        _databaseName = databaseName;
        _databaseId = databaseId;
        _createDate = createDate;
    }

    public static string? ConfiguredServerConnectionString =>
        Environment.GetEnvironmentVariable(ServerConnectionStringVariable);

    public string ConnectionString { get; }

    public static async Task<SqlServerTestDatabase> CreateAsync()
    {
        var configured = ConfiguredServerConnectionString;

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException($"{ServerConnectionStringVariable} is not set.");
        }

        var server = new SqlConnectionStringBuilder(configured);

        if (!string.IsNullOrEmpty(server.AttachDBFilename))
        {
            throw new InvalidOperationException(
                $"{ServerConnectionStringVariable} must not use AttachDbFilename.");
        }

        if (!string.IsNullOrEmpty(server.InitialCatalog))
        {
            throw new InvalidOperationException(
                $"{ServerConnectionStringVariable} must name a server only, not a database.");
        }

        var masterConnectionString = new SqlConnectionStringBuilder(server.ConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;

        var databaseName = $"{DatabaseNamePrefix}{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(server.ConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;

        int databaseId;
        DateTime createDate;

        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync();

            var machineName = (string)(await ScalarAsync(master, "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128));"))!;

            if (!string.Equals(machineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{ServerConnectionStringVariable} must point to a SQL Server on this machine.");
            }

            if (await ScalarAsync(master, "SELECT DB_ID(@name);", databaseName) is not DBNull)
            {
                throw new InvalidOperationException($"Database {databaseName} already exists; refusing to use it.");
            }

            await ExecuteAsync(
                master,
                "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N';'; EXEC (@sql);",
                databaseName);

            (databaseId, createDate) = await ReadIdentityAsync(master, databaseName)
                ?? throw new InvalidOperationException($"Database {databaseName} was not created.");
        }

        var database = new SqlServerTestDatabase(
            masterConnectionString,
            connectionString,
            databaseName,
            databaseId,
            createDate);

        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }

        return database;
    }

    public DentalLoyaltyDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new DentalLoyaltyDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_databaseName.StartsWith(DatabaseNamePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to drop {_databaseName}: not a generated test database.");
        }

        // Close pooled connections to the test database so it can be dropped.
        SqlConnection.ClearPool(new SqlConnection(ConnectionString));

        await using var master = new SqlConnection(_masterConnectionString);
        await master.OpenAsync();

        // Identity check and drop in one batch: only the exact database this
        // instance created is dropped.
        await using var command = new SqlCommand(
            """
            IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @name AND database_id = @id AND create_date = @created)
            BEGIN
                DECLARE @sql nvarchar(max) =
                    N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ' +
                    N'DROP DATABASE ' + QUOTENAME(@name) + N';';
                EXEC (@sql);
                SELECT 1;
            END
            ELSE IF DB_ID(@name) IS NOT NULL
                SELECT -1;
            ELSE
                SELECT 0;
            """,
            master);
        command.Parameters.AddWithValue("@name", _databaseName);
        command.Parameters.AddWithValue("@id", _databaseId);
        command.Parameters.Add("@created", System.Data.SqlDbType.DateTime).Value = _createDate;

        var result = (int)(await command.ExecuteScalarAsync())!;

        if (result == -1)
        {
            throw new InvalidOperationException(
                $"Refusing to drop {_databaseName}: a database with that name exists but is not the one this test created.");
        }
    }

    private static async Task<(int DatabaseId, DateTime CreateDate)?> ReadIdentityAsync(SqlConnection master, string databaseName)
    {
        await using var command = new SqlCommand(
            "SELECT database_id, create_date FROM sys.databases WHERE name = @name;",
            master);
        command.Parameters.AddWithValue("@name", databaseName);

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? (reader.GetInt32(0), reader.GetDateTime(1))
            : null;
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql, string? name = null)
    {
        await using var command = new SqlCommand(sql, connection);

        if (name is not null)
        {
            command.Parameters.AddWithValue("@name", name);
        }

        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, string name)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", name);
        await command.ExecuteNonQueryAsync();
    }
}
