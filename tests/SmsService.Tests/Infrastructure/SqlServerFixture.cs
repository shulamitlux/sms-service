using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmsService.Api.Data;
using Testcontainers.MsSql;

namespace SmsService.Tests.Infrastructure;

/// <summary>
/// One real SQL Server container for the whole test run (rowversion and filtered indexes
/// are not supported by SQLite/InMemory). Every test gets its own fresh database.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates a new, migrated database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "test_" + Guid.NewGuid().ToString("N")
        }.ConnectionString;

        await using var db = CreateContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public static AppDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
