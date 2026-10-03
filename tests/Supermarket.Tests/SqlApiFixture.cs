using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Supermarket.Tests;

public sealed class SqlApiFixture : IAsyncLifetime
{
    public string DatabaseName { get; } = $"FA26SE103_MF01_Test_{Guid.NewGuid():N}";
    public string ConnectionString { get; private set; } = "";
    public const string AdminEmail = "admin@mf01.test";
    public const string AdminPassword = "Test-password-123!";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    private string master = "";
    public async Task InitializeAsync()
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "FA26SE103_Database_V0.1.sql");
        if (!File.Exists(schemaPath))
            throw new FileNotFoundException("SQL integration tests require the shared Project/DB schema. Build with -p:Mf01SchemaPath=<approved SQL file> if it is stored elsewhere.", schemaPath);
        var migrationPath = Path.Combine(AppContext.BaseDirectory, "monitoring_rules_erd_v3.sql");
        if (!File.Exists(migrationPath))
            throw new FileNotFoundException("SQL tests require the Database team's approved monitoring migration. Build with -p:Mf01MonitoringMigrationPath=<approved migration>.", migrationPath);
        var server = Environment.GetEnvironmentVariable("MF01_TEST_SERVER") ?? @".\SQLEXPRESS";
        var configured = Environment.GetEnvironmentVariable("MF01_TEST_CONNECTION");
        var builder = configured is null ? new SqlConnectionStringBuilder { DataSource = server, IntegratedSecurity = true, TrustServerCertificate = true } : new SqlConnectionStringBuilder(configured);
        builder.InitialCatalog = "master";
        master = builder.ConnectionString;
        builder.InitialCatalog = DatabaseName;
        ConnectionString = builder.ConnectionString;
        var sql = (await File.ReadAllTextAsync(schemaPath)).Replace("FA26SE103", DatabaseName);
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }
        await using (var testDatabase = new SqlConnection(ConnectionString))
        {
            await testDatabase.OpenAsync();
            await using var syncZone = new SqlCommand("""
                IF COL_LENGTH(N'dbo.Zone', N'color_hex') IS NULL
                    ALTER TABLE dbo.[Zone] ADD color_hex nvarchar(7) NULL;
                IF COL_LENGTH(N'dbo.Zone', N'area_m2') IS NULL
                    ALTER TABLE dbo.[Zone] ADD area_m2 decimal(12,2) NULL;
                """, testDatabase);
            await syncZone.ExecuteNonQueryAsync();
            // Retarget only the migration's safety guard to this fixture-owned database.
            // Never apply test DDL or API mutations to the shared Dev database.
            var migration = (await File.ReadAllTextAsync(migrationPath)).Replace("DB_NAME() <> N'FA26SE103_Dev'", $"DB_NAME() <> N'{DatabaseName}'");
            foreach (var batch in Regex.Split(migration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var migrate = new SqlCommand(batch, testDatabase) { CommandTimeout = 60 };
                await migrate.ExecuteNonQueryAsync();
            }
        }
        Factory = new ApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SqlServer"] = ConnectionString,
            ["Jwt:Key"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["Bootstrap:Enabled"] = "true",
            ["Bootstrap:Email"] = AdminEmail,
            ["Bootstrap:Password"] = AdminPassword,
            ["CameraHealth:Enabled"] = "false",
            ["Video:AllowDemo"] = "true",
            ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), DatabaseName, "keys")
        });
        _ = Factory.CreateClient();
    }
    public async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        if (string.IsNullOrEmpty(master))
            return;
        SqlConnection.ClearAllPools();
        if (!Regex.IsMatch(DatabaseName, @"^FA26SE103_MF01_Test_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unsafe test cleanup target.");
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END;", connection);
        await command.ExecuteNonQueryAsync();
    }
    private sealed class ApiFactory(Dictionary<string, string?> config) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(config));
    }
}
[CollectionDefinition("SqlApi", DisableParallelization = true)]
public sealed class SqlApiCollection : ICollectionFixture<SqlApiFixture>;
