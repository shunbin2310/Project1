using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Project1.MigrationExecutor;

// Both factories pin their target. Production authority lives in the separately
// installed root-managed host, not this library or its connection-string helper.
public sealed class SqlMigrationSession : IAsyncDisposable
{
    public const string LockResource = "Project1.MigrationExecution";
    private readonly SqlConnection connection;
    private readonly string database;
    private readonly string login;
    private readonly string server;
    private readonly CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
    private bool attempted;

    private SqlMigrationSession(SqlConnection connection, string database, string login, string server)
    {
        this.connection = connection;
        this.database = database;
        this.login = login;
        this.server = server;
    }

    public static string CiConnectionString(string database, string login, string password,
        Func<string, string?> environment)
    {
        if (environment("GITHUB_ACTIONS") != "true" || environment("PROJECT1_CI_MIGRATION_TESTS") != "true" ||
            !Regex.IsMatch(database, @"\AProject1CiMigration_[0-9a-f]{32}_upgrade\z") ||
            !Regex.IsMatch(login, @"\AProject1CiExecutor_[0-9a-f]{32}\z") || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Executor requires an explicitly opted-in, owned CI target.");
        }
        return new SqlConnectionStringBuilder
        {
            DataSource = "tcp:127.0.0.1,14333", InitialCatalog = database,
            UserID = login, Password = password, Encrypt = true,
            TrustServerCertificate = true, Pooling = false, ConnectTimeout = 15,
            ConnectRetryCount = 0, ApplicationName = "Project1.CI.Execution"
        }.ConnectionString;
    }

    public static async Task<SqlMigrationSession> OpenCiAsync(string database, string login, string password)
    {
        var connection = new SqlConnection(CiConnectionString(database, login, password,
            Environment.GetEnvironmentVariable));
        return await OpenAsync(connection, database, login, "project1-ci-sqlserver");
    }

    public static string ProductionConnectionString(string password)
    {
        if (!Regex.IsMatch(password, @"\AP1![0-9a-f]{64}\z"))
            throw new InvalidOperationException("Invalid private execution credential.");
        return new SqlConnectionStringBuilder
        {
            DataSource = "tcp:127.0.0.1,1433", InitialCatalog = "Project1Db",
            UserID = "project1_execute", Password = password, Encrypt = true,
            // Fixed local native instance; never a caller-selected remote host.
            TrustServerCertificate = true, Pooling = false, ConnectTimeout = 15,
            ConnectRetryCount = 0, ApplicationName = "Project1.Production.Execution"
        }.ConnectionString;
    }

    public static Task<SqlMigrationSession> OpenProductionAsync(string password) =>
        OpenAsync(new SqlConnection(ProductionConnectionString(password)),
            "Project1Db", "project1_execute", "homelab-server");

    private static async Task<SqlMigrationSession> OpenAsync(SqlConnection connection,
        string database, string login, string server)
    {
        var session = new SqlMigrationSession(connection, database, login, server);
        try
        {
            await connection.OpenAsync(session.deadline.Token);
            await session.AssertIdentityAsync();
            await session.AssertScopedPermissionsAsync();
            await using var command = session.Command("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = @resource,
                    @LockMode = 'Exclusive', @LockOwner = 'Session',
                    @LockTimeout = 0, @DbPrincipal = 'public';
                SELECT @result;
                """);
            command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = LockResource;
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(session.deadline.Token));
            if (result is not (0 or 1)) throw new InvalidOperationException("Database migration lock is unavailable.");
            await session.AssertLockAsync();
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private SqlCommand Command(string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        return command;
    }

    private async Task AssertIdentityAsync()
    {
        await using var command = Command("""
            SELECT CONVERT(nvarchar(128), @@SERVERNAME), DB_NAME(), SUSER_SNAME(),
                CONVERT(int, SERVERPROPERTY('ProductMajorVersion')), @@TRANCOUNT;
            """);
        await using var reader = await command.ExecuteReaderAsync(deadline.Token);
        if (!await reader.ReadAsync(deadline.Token) || reader.GetString(0) != server ||
            reader.GetString(1) != database || reader.GetString(2) != login ||
            reader.GetInt32(3) != 17 || reader.GetInt32(4) != 0)
            throw new InvalidOperationException("Unexpected executor session identity or transaction state.");
        await reader.DisposeAsync();
        await using var state = Command("""
            SELECT state_desc, is_read_only, is_trustworthy_on
            FROM sys.databases WHERE name = DB_NAME();
            """);
        await using var states = await state.ExecuteReaderAsync(deadline.Token);
        if (!await states.ReadAsync(deadline.Token) || states.GetString(0) != "ONLINE" ||
            states.GetBoolean(1) || states.GetBoolean(2))
            throw new InvalidOperationException("Unexpected database state.");
    }

    private async Task AssertLockAsync()
    {
        await using var command = Command("SELECT APPLOCK_MODE('public', @resource, 'Session');");
        command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = LockResource;
        if (!Equals(await command.ExecuteScalarAsync(deadline.Token), "Exclusive"))
            throw new InvalidOperationException("The executor no longer owns the database migration lock.");
    }

    private async Task AssertScopedPermissionsAsync()
    {
        await using var command = Command("""
            SELECT IS_SRVROLEMEMBER('sysadmin'), IS_SRVROLEMEMBER('securityadmin'),
                IS_ROLEMEMBER('db_owner'), HAS_PERMS_BY_NAME(NULL, NULL, 'CONTROL SERVER'),
                HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY LOGIN'),
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'),
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'),
                HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'),
                HAS_PERMS_BY_NAME('dbo.Products', 'OBJECT', 'ALTER'),
                HAS_PERMS_BY_NAME('dbo.__EFMigrationsHistory', 'OBJECT', 'SELECT'),
                HAS_PERMS_BY_NAME('dbo.__EFMigrationsHistory', 'OBJECT', 'INSERT'),
                HAS_PERMS_BY_NAME('dbo.Products', 'OBJECT', 'SELECT'),
                HAS_PERMS_BY_NAME('dbo.Products', 'OBJECT', 'INSERT'),
                HAS_PERMS_BY_NAME('dbo.Products', 'OBJECT', 'UPDATE'),
                HAS_PERMS_BY_NAME('dbo.Products', 'OBJECT', 'DELETE'),
                HAS_PERMS_BY_NAME('dbo.__EFMigrationsHistory', 'OBJECT', 'UPDATE'),
                HAS_PERMS_BY_NAME('dbo.__EFMigrationsHistory', 'OBJECT', 'DELETE');
            """);
        await using var reader = await command.ExecuteReaderAsync(deadline.Token);
        if (!await reader.ReadAsync(deadline.Token)) throw new InvalidOperationException("Cannot verify executor permissions.");
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var expected = index is 8 or 9 or 10 ? 1 : 0;
            if (reader.IsDBNull(index) || reader.GetInt32(index) != expected)
                throw new InvalidOperationException("Unexpected executor permissions; provisioning requires review.");
        }
    }

    public async Task<string[]> HistoryAsync()
    {
        await AssertIdentityAsync();
        await AssertLockAsync();
        await using var command = Command("SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;");
        await using var reader = await command.ExecuteReaderAsync(deadline.Token);
        var result = new List<string>();
        while (await reader.ReadAsync(deadline.Token))
        {
            var id = reader.GetString(0);
            if (result.Count >= 1000 || !Regex.IsMatch(id, @"\A[0-9]{14}_[A-Za-z][A-Za-z0-9_]{0,134}\z") ||
                (result.Count > 0 && string.CompareOrdinal(result[^1], id) >= 0))
                throw new InvalidOperationException("Unexpected executor migration history.");
            result.Add(id);
        }
        return result.ToArray();
    }

    public async Task AssertNoteSchemaAsync(bool after)
    {
        await AssertIdentityAsync();
        await AssertLockAsync();
        await using var command = Command("""
            SELECT OBJECT_ID(N'dbo.Products', N'U');
            SELECT t.name, c.max_length, c.is_nullable
            FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
            WHERE c.object_id = OBJECT_ID(N'dbo.Products', N'U') AND c.name = N'Note';
            """);
        await using var reader = await command.ExecuteReaderAsync(deadline.Token);
        if (!await reader.ReadAsync(deadline.Token) || reader.IsDBNull(0))
            throw new InvalidOperationException("Products table is absent or not visible to the scoped executor.");
        await reader.NextResultAsync(deadline.Token);
        var exists = await reader.ReadAsync(deadline.Token);
        if (after ? !exists || reader.GetString(0) != "nvarchar" || reader.GetInt16(1) != -1 || !reader.GetBoolean(2) : exists)
            throw new InvalidOperationException("Product Note schema does not match the reviewed upgrade.");
    }

    public async Task ApplyAsync(byte[] sqlBytes, string expectedDigest)
    {
        if (attempted) throw new InvalidOperationException("This execution session cannot retry SQL.");
        if (sqlBytes.Length is <= 0 or > 10 * 1024 * 1024)
            throw new InvalidOperationException("Executor SQL size is invalid.");
        var ownedBytes = sqlBytes.ToArray();
        if (!Regex.IsMatch(expectedDigest, @"\A[0-9a-f]{64}\z") ||
            Convert.ToHexStringLower(SHA256.HashData(ownedBytes)) != expectedDigest)
            throw new InvalidOperationException("Executor SQL digest changed.");
        var sql = new UTF8Encoding(false, true).GetString(ownedBytes);
        var batches = SqlBatchParser.SplitBatches(sql);
        await AssertScopedPermissionsAsync();
        await AssertNoteSchemaAsync(after: false);
        attempted = true;
        try
        {
            foreach (var batch in batches)
            {
                // Inline guard preserves transactions that intentionally span GO batches.
                await using var command = Command("SET XACT_ABORT ON;\nBEGIN TRY\n" + batch +
                    "\nEND TRY\nBEGIN CATCH\nIF XACT_STATE() <> 0 ROLLBACK TRANSACTION;\nTHROW;\nEND CATCH;");
                await command.ExecuteNonQueryAsync(deadline.Token);
                // Checking only the lock here permits an open transaction between batches.
                await AssertLockAsync();
            }
            await AssertIdentityAsync(); // No success with an uncommitted final batch.
            await AssertNoteSchemaAsync(after: true);
        }
        catch (Exception executionError)
        {
            // Compile errors can bypass the inline CATCH; clean up on the same connection.
            try { await RollbackAsync(); }
            catch (Exception cleanupError)
            {
                // Do not reuse a session with uncertain cleanup or replace its original error.
                await connection.CloseAsync();
                throw new AggregateException("Executor SQL and transaction cleanup failed.", executionError, cleanupError);
            }
            throw;
        }
    }

    private async Task RollbackAsync()
    {
        if (connection.State != ConnectionState.Open) return;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var command = Command("IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;");
        command.CommandTimeout = 10;
        await command.ExecuteNonQueryAsync(cleanup.Token);
    }

    public async ValueTask DisposeAsync()
    {
        try { await RollbackAsync(); }
        finally
        {
            await connection.DisposeAsync(); // Unpooled: terminates transaction/session-owned lock.
            deadline.Dispose();
        }
    }
}
