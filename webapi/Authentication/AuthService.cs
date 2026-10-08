using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace ServerActivity.Api.Authentication;

public sealed class AuthService(
    IPasswordHasher<Account> passwordHasher,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ISecurityBackupService securityBackup,
    ILogger<AuthService> logger)
{
    private readonly string _connectionString = CreateDatabase(environment, configuration);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();
    private readonly ConcurrentDictionary<string, string> _tokenOwners = new();
    private readonly HashSet<string> _administratorLogins = ParseAdministratorLogins(configuration);

    private static HashSet<string> ParseAdministratorLogins(IConfiguration configuration)
    {
        var configuredLogins = configuration["Security:AdminLogins"];
        return string.IsNullOrWhiteSpace(configuredLogins)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : configuredLogins.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateDatabase(
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        var path = configuration["Authentication:DatabasePath"] ??
            Path.Combine(environment.ContentRootPath, "data", "users.db");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Accounts (
                Login TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                PasswordHash TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS LoginHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Login TEXT NOT NULL COLLATE NOCASE,
                IpAddress TEXT NULL,
                CreatedAt TEXT NOT NULL,
                IsNewIp INTEGER NOT NULL DEFAULT 0,
                BackupStatus TEXT NOT NULL DEFAULT 'not-required'
            );
            CREATE INDEX IF NOT EXISTS IX_LoginHistory_Login_Id
                ON LoginHistory (Login, Id DESC);
            """;
        command.ExecuteNonQuery();
        return connectionString;
    }

    public bool Register(string login, string password, string? ipAddress)
    {
        var account = new Account(login, string.Empty);
        account.PasswordHash = passwordHasher.HashPassword(account, password);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Accounts (Login, PasswordHash) VALUES ($login, $hash)
            ON CONFLICT(Login) DO NOTHING
            RETURNING Login;
            """;
        command.Parameters.AddWithValue("$login", login);
        command.Parameters.AddWithValue("$hash", account.PasswordHash);
        if (command.ExecuteScalar() is null)
        {
            return false;
        }

        InsertLoginHistory(connection, transaction, login, ipAddress, false, "not-required");
        transaction.Commit();
        return true;
    }

    public AuthenticatedLogin? Login(string login, string password, string? ipAddress)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PasswordHash FROM Accounts WHERE Login = $login;";
        command.Parameters.AddWithValue("$login", login);
        var passwordHash = command.ExecuteScalar() as string;
        var account = passwordHash is null ? null : new Account(login, passwordHash);

        if (account is null ||
            passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password)
                == PasswordVerificationResult.Failed)
        {
            return null;
        }

        ipAddress = NormalizeIpAddress(ipAddress);
        var previousLogin = GetLatestLogin(connection, login);
        var isNewIp = ipAddress is not null &&
            previousLogin?.IpAddress is not null &&
            !string.Equals(previousLogin.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase);
        var shouldCreateBackup = isNewIp ||
            previousLogin?.BackupStatus is "failed" or "pending";
        var historyId = InsertLoginHistory(
            connection,
            null,
            account.Login,
            ipAddress,
            isNewIp,
            shouldCreateBackup ? "pending" : "not-required");
        string? securityWarning = null;

        if (shouldCreateBackup)
        {
            try
            {
                securityBackup.CreateEncryptedBackup();
                UpdateBackupStatus(connection, historyId, "created");
            }
            catch (SecureBackupException exception)
            {
                UpdateBackupStatus(connection, historyId, "failed");
                logger.LogError(
                    exception,
                    "Unable to create encrypted backup after a login from a new IP for {Login}.",
                    account.Login);
                securityWarning = "Wykryto logowanie z nowego adresu IP, ale nie udało się utworzyć zaszyfrowanego backupu. Spróbujemy ponownie przy kolejnym logowaniu.";
            }
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = DateTimeOffset.UtcNow.AddHours(1);
        _tokenOwners[token] = account.Login;
        return new AuthenticatedLogin(token, _administratorLogins.Contains(account.Login), securityWarning);
    }

    public IReadOnlyList<LoginHistoryRecord> GetLoginHistory(string login)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT IpAddress, CreatedAt, IsNewIp, BackupStatus
            FROM LoginHistory
            WHERE Login = $login
            ORDER BY Id DESC
            LIMIT 100;
            """;
        command.Parameters.AddWithValue("$login", login);
        using var reader = command.ExecuteReader();
        var entries = new List<LoginHistoryRecord>();
        while (reader.Read())
        {
            entries.Add(new LoginHistoryRecord(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt64(2) != 0,
                reader.GetString(3)));
        }

        return entries;
    }

    private static string? NormalizeIpAddress(string? ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            return null;
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    private static LoginHistoryRecord? GetLatestLogin(SqliteConnection connection, string login)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT IpAddress, CreatedAt, IsNewIp, BackupStatus
            FROM LoginHistory
            WHERE Login = $login
            ORDER BY Id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$login", login);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new LoginHistoryRecord(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt64(2) != 0,
                reader.GetString(3))
            : null;
    }

    private static long InsertLoginHistory(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string login,
        string? ipAddress,
        bool isNewIp,
        string backupStatus)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LoginHistory (Login, IpAddress, CreatedAt, IsNewIp, BackupStatus)
            VALUES ($login, $ipAddress, $createdAt, $isNewIp, $backupStatus);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$login", login);
        command.Parameters.AddWithValue("$ipAddress", (object?)ipAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$isNewIp", isNewIp ? 1 : 0);
        command.Parameters.AddWithValue("$backupStatus", backupStatus);
        return (long)command.ExecuteScalar()!;
    }

    private static void UpdateBackupStatus(SqliteConnection connection, long historyId, string status)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE LoginHistory SET BackupStatus = $status WHERE Id = $id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", historyId);
        command.ExecuteNonQuery();
    }

    public string? FindLogin(string token)
    {
        if (!_tokens.TryGetValue(token, out var expiresAt))
        {
            return null;
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            _tokens.TryRemove(token, out _);
            _tokenOwners.TryRemove(token, out _);
            return null;
        }

        return _tokenOwners.TryGetValue(token, out var login) ? login : null;
    }

    public bool IsAdministrator(string login) => _administratorLogins.Contains(login);
}

public sealed record AuthenticatedLogin(string Token, bool IsAdministrator, string? SecurityWarning);

public sealed record LoginHistoryRecord(
    string? IpAddress,
    DateTimeOffset CreatedAt,
    bool IsNewIp,
    string BackupStatus);
