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
    private static readonly SemaphoreSlim CurrentSchemaGate = new(1, 1);
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
        var currentSchemaDirectory = Path.Combine(AppContext.BaseDirectory, "CurrentSchema");
        if (CurrentSchemaAvailable(currentSchemaDirectory))
        {
            await CurrentSchemaGate.WaitAsync();
            try { await InitializeCurrentSchema(currentSchemaDirectory); }
            finally { CurrentSchemaGate.Release(); }
            return;
        }
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

    private static readonly string[] CurrentSchemaScripts =
    [
        "Backup18102026.sql",
        "FA26SE103_Database_V2.sql",
        "FA26SE103_Database_V3.sql",
        "FA26SE103_Database_V4.sql",
        "FA26SE103_Database_V5.sql",
        "FA26SE103_Database_V6_Cleanup.sql",
        "FA26SE103_Database_V7_Weekly_Work_Shifts.sql",
        "FA26SE103_Database_V8_Notification_Settings.sql",
        "FA26SE103_Database_V9_Migration.sql",
        "FA26SE103_Database_V10_Seeds.sql",
        "FA26SE103_Database_V11_Operational_SLA_Cutover.sql",
        "FA26SE103_Database_V12_StaffReport_IncidentType.sql",
        "FA26SE103_Database_V13_Incident_Duplicate_Choice.sql",
        "FA26SE103_Database_V14_Monitoring_Incident_Uniqueness.sql",
        "FA26SE103_Database_V15_StaffReport_Category.sql",
        "FA26SE103_Database_V16_Standalone_Task_Policy.sql"
    ];

    private static bool CurrentSchemaAvailable(string directory) =>
        CurrentSchemaScripts.All(file => File.Exists(Path.Combine(directory, file)));

    private async Task InitializeCurrentSchema(string directory)
    {
        var server = Environment.GetEnvironmentVariable("MF01_TEST_SERVER") ?? @".\SQLEXPRESS";
        var configured = Environment.GetEnvironmentVariable("MF01_TEST_CONNECTION");
        var builder = configured is null
            ? new SqlConnectionStringBuilder { DataSource = server, IntegratedSecurity = true, TrustServerCertificate = true }
            : new SqlConnectionStringBuilder(configured);
        builder.InitialCatalog = "master";
        master = builder.ConnectionString;
        builder.InitialCatalog = DatabaseName;
        ConnectionString = builder.ConnectionString;

        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using (var create = new SqlCommand($"CREATE DATABASE [{DatabaseName}]", connection) { CommandTimeout = 60 })
            await create.ExecuteNonQueryAsync();

        await using (var testDatabase = new SqlConnection(ConnectionString))
        {
            await testDatabase.OpenAsync();
            var backup = await File.ReadAllTextAsync(Path.Combine(directory, CurrentSchemaScripts[0]));
            var reachedDatabaseBody = false;
            foreach (var originalBatch in Batches(backup))
            {
                if (!reachedDatabaseBody)
                {
                    if (Regex.IsMatch(originalBatch, @"^\s*USE\s+\[FA26SE103_Dev\]", RegexOptions.IgnoreCase))
                        reachedDatabaseBody = true;
                    else
                        continue;
                }
                if (Regex.IsMatch(originalBatch, @"\bCREATE\s+USER\s+\[shiori\]", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(originalBatch, @"\bALTER\s+ROLE\s+\[db_owner\].*\[shiori\]", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    continue;
                await ExecuteBatch(testDatabase, Retarget(originalBatch));
            }

            // SQL Server-generated backups may end by switching back to master.
            // Keep migration guards and DDL scoped to the fixture-owned catalog.
            testDatabase.ChangeDatabase(DatabaseName);

            foreach (var file in CurrentSchemaScripts.Skip(1))
            {
                if (await MigrationAlreadyApplied(testDatabase, file))
                    continue;
                if (file == "FA26SE103_Database_V6_Cleanup.sql")
                {
                    // The historical 18/10 snapshot contains one obsolete demo Incident whose
                    // source cannot satisfy the V6 provenance constraint. Production cleanup used
                    // the same reviewed removal; fixture tests seed all operational rows themselves.
                    await ExecuteBatch(testDatabase, "UPDATE dbo.OperationalEvent SET incident_id=NULL WHERE incident_id IS NOT NULL; DELETE FROM dbo.Incident;");
                }
                var script = Retarget(await File.ReadAllTextAsync(Path.Combine(directory, file)));
                if (file == "FA26SE103_Database_V11_Operational_SLA_Cutover.sql")
                    script = CommitV11InDisposableFixture(script);
                foreach (var batch in Batches(script))
                    await ExecuteBatch(testDatabase, batch);
            }
            await ResetFixtureData(testDatabase);
        }

        ConfigureFactory();
    }

    private string CommitV11InDisposableFixture(string sql) => sql
        // The fixture database is newly created, isolated and deleted after the test. These
        // substitutions exercise V11's real commit/drop path without asserting anything about
        // backup or writer state on a shared/live database.
        .Replace("DECLARE @ApplyChanges bit = 0;", "DECLARE @ApplyChanges bit = 1;", StringComparison.Ordinal)
        .Replace("DECLARE @DropLegacyTables bit = 0;", "DECLARE @DropLegacyTables bit = 1;", StringComparison.Ordinal)
        .Replace("DECLARE @ExpectedDatabase sysname = NULL;", $"DECLARE @ExpectedDatabase sysname = N'{DatabaseName}';", StringComparison.Ordinal)
        .Replace("DECLARE @BackupVerified bit = 0;", "DECLARE @BackupVerified bit = 1;", StringComparison.Ordinal)
        .Replace("DECLARE @WritersStopped bit = 0;", "DECLARE @WritersStopped bit = 1;", StringComparison.Ordinal)
        .Replace("DECLARE @SlaBackendVerified bit = 0;", "DECLARE @SlaBackendVerified bit = 1;", StringComparison.Ordinal);

    private string Retarget(string sql) => sql
        .Replace("[FA26SE103_Dev]", $"[{DatabaseName}]", StringComparison.OrdinalIgnoreCase)
        .Replace("N'FA26SE103_Dev'", $"N'{DatabaseName}'", StringComparison.OrdinalIgnoreCase)
        .Replace("'FA26SE103_Dev'", $"'{DatabaseName}'", StringComparison.OrdinalIgnoreCase)
        // Named constraints on local temp tables live in tempdb and can collide when
        // multiple fixture databases are prepared by separate test classes.
        .Replace("PK_ShiftWeekMap", $"PK_ShiftWeekMap_{DatabaseName[^8..]}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Batches(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch));

    private static async Task ExecuteBatch(SqlConnection connection, string batch)
    {
        await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> MigrationAlreadyApplied(SqlConnection connection, string file)
    {
        var predicate = file switch
        {
            "FA26SE103_Database_V2.sql" => "OBJECT_ID(N'dbo.OperationalEventType', N'U') IS NOT NULL",
            "FA26SE103_Database_V3.sql" => "COL_LENGTH(N'dbo.Task', N'planned_start_at') IS NOT NULL",
            "FA26SE103_Database_V4.sql" => "OBJECT_ID(N'dbo.UserDevice', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.ShiftSwapRequest', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.StaffReportMedia', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ResponsePolicy', N'incident_type_id') IS NOT NULL AND COL_LENGTH(N'dbo.ResponsePolicy', N'incident_severity') IS NOT NULL",
            "FA26SE103_Database_V5.sql" => "OBJECT_ID(N'dbo.SystemSetting', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.ServiceHealthEvent', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MonitoringRule', N'outcome_type') IS NOT NULL",
            "FA26SE103_Database_V6_Cleanup.sql" => "OBJECT_ID(N'dbo.NotificationConfig', N'U') IS NULL AND OBJECT_ID(N'dbo.ZoneShiftRequirement', N'U') IS NULL AND COL_LENGTH(N'dbo.OperationalEvent', N'incident_id') IS NULL",
            "FA26SE103_Database_V7_Weekly_Work_Shifts.sql" => "COL_LENGTH(N'dbo.Shift', N'week_start') IS NOT NULL",
            "FA26SE103_Database_V9_Migration.sql" => "OBJECT_ID(N'dbo.StaffSpecialty', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TaskType', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TaskOffer', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SLAPolicy', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SLAPolicyAction', N'U') IS NOT NULL",
            "FA26SE103_Database_V11_Operational_SLA_Cutover.sql" => "OBJECT_ID(N'dbo.ResponsePolicy', N'U') IS NULL AND OBJECT_ID(N'dbo.ResponsePolicyAction', N'U') IS NULL AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SLAPolicy') AND name=N'UX_SLAPolicy_Enabled_OperationalEventType')",
            "FA26SE103_Database_V12_StaffReport_IncidentType.sql" => "COL_LENGTH(N'dbo.StaffReport', N'incident_type_id') IS NOT NULL AND COL_LENGTH(N'dbo.StaffReport', N'classification') IS NULL",
            "FA26SE103_Database_V13_Incident_Duplicate_Choice.sql" => "NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Incident') AND name=N'UX_Incident_Open_Zone_Type')",
            "FA26SE103_Database_V14_Monitoring_Incident_Uniqueness.sql" => "EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Incident') AND name=N'UX_Incident_Open_MonitoringRule_Zone_Type')",
            "FA26SE103_Database_V15_StaffReport_Category.sql" => "COL_LENGTH(N'dbo.StaffReport', N'report_category') IS NOT NULL",
            "FA26SE103_Database_V16_Standalone_Task_Policy.sql" => "COL_LENGTH(N'dbo.SLAPolicy', N'task_type_id') IS NOT NULL AND (SELECT COUNT(*) FROM dbo.SLAPolicy WHERE problem_domain=N'STANDALONE_TASK' AND is_enabled=1)=11",
            _ => null
        };
        if (predicate is null) return false;
        await using var command = new SqlCommand($"SELECT CONVERT(bit, CASE WHEN {predicate} THEN 1 ELSE 0 END)", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static Task ResetFixtureData(SqlConnection connection) => ExecuteBatch(connection, """
        DECLARE @sql nvarchar(max)=N'';
        SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N' NOCHECK CONSTRAINT ALL;'
        FROM sys.tables WHERE is_ms_shipped=0;
        EXEC sys.sp_executesql @sql;

        SET @sql=N'';
        SELECT @sql += N'DELETE FROM ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';'
        FROM sys.tables
        WHERE is_ms_shipped=0 AND name NOT IN
        (N'Role',N'IncidentType',N'OperationalEventType',N'StaffSpecialty',N'TaskType',N'SystemSetting',N'SLAPolicy',N'SLAPolicyAction');
        EXEC sys.sp_executesql @sql;

        SET @sql=N'';
        SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N' WITH CHECK CHECK CONSTRAINT ALL;'
        FROM sys.tables WHERE is_ms_shipped=0;
        EXEC sys.sp_executesql @sql;
        """);

    private void ConfigureFactory()
    {
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
