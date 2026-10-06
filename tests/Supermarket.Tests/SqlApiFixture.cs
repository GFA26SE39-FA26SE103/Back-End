using System.Text.RegularExpressions;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Supermarket.Application;
using Supermarket.Domain;
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
    private string? adminToken;
    public async Task<HttpClient> AdminClient()
    {
        var client=Factory.CreateClient();
        if(adminToken is null)
        {
            var response=await client.PostAsJsonAsync("/api/auth/login",new Supermarket.Application.LoginRequest(AdminEmail,AdminPassword));
            response.EnsureSuccessStatusCode();
            adminToken=(await response.Content.ReadFromJsonAsync<Supermarket.Application.LoginResponse>())!.AccessToken;
        }
        client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",adminToken);
        return client;
    }
    public async Task InitializeAsync()
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "FA26SE103_Database_V0.1.sql");
        if (!File.Exists(schemaPath))
            throw new FileNotFoundException("SQL integration tests require the shared Project/DB schema. Build with -p:Mf01SchemaPath=<approved SQL file> if it is stored elsewhere.", schemaPath);
        var migrationPath = Path.Combine(AppContext.BaseDirectory, "monitoring_rules_erd_v3.sql");
        if (!File.Exists(migrationPath))
            throw new FileNotFoundException("SQL tests require the Database team's approved monitoring migration. Build with -p:Mf01MonitoringMigrationPath=<approved migration>.", migrationPath);
        var runtimeMigrationPath = Path.Combine(AppContext.BaseDirectory, "monitoring_runtime_erd_v3.sql");
        if (!File.Exists(runtimeMigrationPath))
            throw new FileNotFoundException("SQL tests require runtime migration 02. Set Mf02MonitoringRuntimeMigrationPath to the approved SQL file.", runtimeMigrationPath);
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
            var runtimeMigration = (await File.ReadAllTextAsync(runtimeMigrationPath)).Replace("DB_NAME() <> N'FA26SE103_Dev'", $"DB_NAME() <> N'{DatabaseName}'");
            await using var runtime = new SqlCommand(runtimeMigration, testDatabase) { CommandTimeout = 60 };
            await runtime.ExecuteNonQueryAsync();
        }
        Factory = new ApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SqlServer"] = ConnectionString,
            ["Jwt:Key"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["Bootstrap:Enabled"] = "true",
            ["Bootstrap:Email"] = AdminEmail,
            ["Bootstrap:Password"] = AdminPassword,
            ["CameraHealth:Enabled"] = "false",
            ["Monitoring:Enabled"] = "false",
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
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(config))
            .ConfigureServices(services =>
            {
                services.RemoveAll<ICameraStream>();
                services.AddSingleton<TestCameraStream>();
                services.AddSingleton<ICameraStream>(provider => provider.GetRequiredService<TestCameraStream>());
            });
    }

    public sealed class TestCameraStream : ICameraStream
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> online = new();
        public void Set(Guid cameraId, bool value) => online[cameraId] = value;
        public Task<ProbeResult> Test(CameraConnection connection, CancellationToken ct)
        {
            var success = !online.TryGetValue(connection.CameraId, out var value) || value;
            return Task.FromResult(new ProbeResult(success, success ? "FRAME_RECEIVED" : "STREAM_UNAVAILABLE", success ? true : null, []));
        }
        public Task<PreviewFrame> Preview(CameraConnection connection, CancellationToken ct) =>
            Task.FromResult(new PreviewFrame([1, 2, 3], "image/jpeg"));
    }
}
[CollectionDefinition("SqlApi", DisableParallelization = true)]
public sealed class SqlApiCollection : ICollectionFixture<SqlApiFixture>;
