namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// Runs a test only when a SQL Server for disposable test databases is configured
// in DENTALLOYALTY_TEST_SQLSERVER (a server connection string). Otherwise the
// test is reported as skipped - never silently passed, and never pointed at a
// default or development database.
public sealed class SqlServerIntegrationFactAttribute : FactAttribute
{
    public SqlServerIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(SqlServerTestDatabase.ConfiguredServerConnectionString))
        {
            Skip = $"SQL Server integration test: set {SqlServerTestDatabase.ServerConnectionStringVariable} to a SQL Server connection string for disposable test databases.";
        }
    }
}
