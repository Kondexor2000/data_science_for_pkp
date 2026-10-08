using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace ServerActivity.Api.LocalPlaytime;

public sealed record RunningProcess(string ProcessName);

public sealed record TrackedGameUser(
    long Id,
    string UserName,
    double TotalSeconds,
    bool IsActive);

public sealed record TrackedGame(
    long Id,
    string GameName,
    string ProcessName,
    double TotalSeconds,
    bool IsRunning,
    bool IsTracking,
    IReadOnlyList<TrackedGameUser> Users);

public sealed class LocalPlaytimeService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private readonly string _connectionString;
    private readonly ILogger<LocalPlaytimeService> _logger;

    public LocalPlaytimeService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<LocalPlaytimeService> logger)
    {
        _logger = logger;
        var path = configuration["LocalPlaytime:DatabasePath"] ??
            Path.Combine(environment.ContentRootPath, "data", "playtime.db");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        InitializeDatabase();
    }

    public IReadOnlyList<RunningProcess> GetRunningProcesses()
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == currentSessionId)
                    {
                        names.Add(process.ProcessName);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the process list was being read.
                }
                catch (Win32Exception exception)
                {
                    _logger.LogDebug(exception, "Nie można odczytać procesu {ProcessId}.", process.Id);
                }
            }
        }

        return names.Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => new RunningProcess(name))
            .ToArray();
    }

    public long? AddTrackedGame(string login, string gameName, string processName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrackedGames (Login, GameName, ProcessName, IsTracking)
            VALUES ($login, $gameName, $processName, 1)
            ON CONFLICT(Login, ProcessName) DO UPDATE
            SET GameName = excluded.GameName, IsTracking = 1
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$login", login);
        command.Parameters.AddWithValue("$gameName", gameName);
        command.Parameters.AddWithValue("$processName", processName);
        var result = command.ExecuteScalar();
        return result is long id ? id : null;
    }

    public IReadOnlyList<TrackedGame> GetTrackedGames(string login)
    {
        var now = DateTimeOffset.UtcNow;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT game.Id, game.GameName, game.ProcessName,
                COALESCE(SUM(
                    (julianday(COALESCE(session.EndedAt, $now)) -
                     julianday(session.StartedAt)) * 86400
                ), 0),
                MAX(CASE WHEN session.EndedAt IS NULL THEN 1 ELSE 0 END),
                game.IsTracking
            FROM TrackedGames AS game
            LEFT JOIN GameSessions AS session ON session.TrackedGameId = game.Id
            WHERE game.Login = $login
            GROUP BY game.Id, game.GameName, game.ProcessName
            ORDER BY game.GameName COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$login", login);

        var games = new List<TrackedGame>();
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                games.Add(new TrackedGame(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDouble(3),
                    reader.GetInt64(4) == 1,
                    reader.GetInt64(5) == 1,
                    []));
            }
        }

        using var usersCommand = connection.CreateCommand();
        usersCommand.CommandText = """
            SELECT user.Id, user.TrackedGameId, user.UserName,
                COALESCE(SUM(
                    (julianday(COALESCE(session.EndedAt, $now)) -
                     julianday(session.StartedAt)) * 86400
                ), 0),
                user.IsActive
            FROM GameUsers AS user
            JOIN TrackedGames AS game ON game.Id = user.TrackedGameId
            LEFT JOIN GameSessions AS session ON session.GameUserId = user.Id
            WHERE game.Login = $login
            GROUP BY user.Id, user.TrackedGameId, user.UserName, user.IsActive
            ORDER BY user.UserName COLLATE NOCASE;
            """;
        usersCommand.Parameters.AddWithValue("$now", now.ToString("O"));
        usersCommand.Parameters.AddWithValue("$login", login);
        using var usersReader = usersCommand.ExecuteReader();
        var usersByGame = new Dictionary<long, List<TrackedGameUser>>();
        while (usersReader.Read())
        {
            var gameId = usersReader.GetInt64(1);
            if (!usersByGame.TryGetValue(gameId, out var users))
            {
                users = [];
                usersByGame.Add(gameId, users);
            }

            users.Add(new TrackedGameUser(
                usersReader.GetInt64(0),
                usersReader.GetString(2),
                usersReader.GetDouble(3),
                usersReader.GetInt64(4) == 1));
        }

        return games
            .Select(game => game with
            {
                Users = usersByGame.GetValueOrDefault(game.Id, [])
            })
            .ToArray();
    }

    public long? AddGameUser(string login, long trackedGameId, string userName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GameUsers (TrackedGameId, UserName, IsActive)
            SELECT game.Id, $userName,
                CASE WHEN EXISTS (
                    SELECT 1 FROM GameUsers
                    WHERE TrackedGameId = game.Id AND IsActive = 1
                ) THEN 0 ELSE 1 END
            FROM TrackedGames AS game
            WHERE game.Id = $gameId AND game.Login = $login
            ON CONFLICT(TrackedGameId, UserName) DO NOTHING
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$userName", userName);
        command.Parameters.AddWithValue("$gameId", trackedGameId);
        command.Parameters.AddWithValue("$login", login);
        var result = command.ExecuteScalar();
        return result is long id ? id : null;
    }

    public bool OwnsTrackedGame(string login, long trackedGameId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM TrackedGames WHERE Id = $gameId AND Login = $login
            );
            """;
        command.Parameters.AddWithValue("$gameId", trackedGameId);
        command.Parameters.AddWithValue("$login", login);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    public bool SetActiveGameUser(string login, long trackedGameId, long userId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE GameUsers SET IsActive = 0
            WHERE TrackedGameId = $gameId
                AND EXISTS (
                    SELECT 1 FROM TrackedGames
                    WHERE Id = $gameId AND Login = $login
                );
            """;
        command.Parameters.AddWithValue("$gameId", trackedGameId);
        command.Parameters.AddWithValue("$login", login);
        command.ExecuteNonQuery();

        command.CommandText = """
            UPDATE GameUsers SET IsActive = 1
            WHERE Id = $userId AND TrackedGameId = $gameId
                AND EXISTS (
                    SELECT 1 FROM TrackedGames
                    WHERE Id = $gameId AND Login = $login
                );
            """;
        command.Parameters.AddWithValue("$userId", userId);
        if (command.ExecuteNonQuery() == 0)
        {
            transaction.Rollback();
            return false;
        }

        command.CommandText = """
            UPDATE GameSessions SET EndedAt = LastSeenAt
            WHERE TrackedGameId = $gameId AND EndedAt IS NULL;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public bool RemoveTrackedGame(string login, long id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE TrackedGames SET IsTracking = 0 " +
            "WHERE Id = $id AND Login = $login AND IsTracking = 1;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$login", login);
        if (command.ExecuteNonQuery() == 0)
        {
            return false;
        }

        command.CommandText = """
            UPDATE GameSessions SET EndedAt = LastSeenAt
            WHERE TrackedGameId = $id AND EndedAt IS NULL;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        CloseUnfinishedSessions();
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            PollProcesses();
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        CloseUnfinishedSessions();
    }

    private void InitializeDatabase()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS TrackedGames (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Login TEXT NOT NULL COLLATE NOCASE,
                GameName TEXT NOT NULL,
                ProcessName TEXT NOT NULL COLLATE NOCASE,
                IsTracking INTEGER NOT NULL DEFAULT 1,
                UNIQUE (Login, ProcessName)
            );
            CREATE TABLE IF NOT EXISTS GameSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TrackedGameId INTEGER NOT NULL REFERENCES TrackedGames(Id) ON DELETE CASCADE,
                StartedAt TEXT NOT NULL,
                LastSeenAt TEXT NOT NULL,
                EndedAt TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS GameUsers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TrackedGameId INTEGER NOT NULL REFERENCES TrackedGames(Id) ON DELETE CASCADE,
                UserName TEXT NOT NULL COLLATE NOCASE,
                IsActive INTEGER NOT NULL DEFAULT 0,
                UNIQUE (TrackedGameId, UserName)
            );
            """;
        command.ExecuteNonQuery();

        using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(GameSessions);";
        var hasUserColumn = false;
        {
            using var columnsReader = columnsCommand.ExecuteReader();
            while (columnsReader.Read())
            {
                if (columnsReader.GetString(1) == "GameUserId")
                {
                    hasUserColumn = true;
                    break;
                }
            }
        }

        if (!hasUserColumn)
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = """
                ALTER TABLE GameSessions
                ADD COLUMN GameUserId INTEGER NULL REFERENCES GameUsers(Id);
                """;
            alterCommand.ExecuteNonQuery();
        }

        using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_GameSessions_OneOpenSession
                ON GameSessions (TrackedGameId) WHERE EndedAt IS NULL;
            """;
        indexCommand.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private void PollProcesses()
    {
        var running = GetRunningProcesses()
            .Select(process => process.ProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT game.Id, game.ProcessName,
                (SELECT user.Id FROM GameUsers AS user
                 WHERE user.TrackedGameId = game.Id AND user.IsActive = 1)
            FROM TrackedGames AS game
            WHERE game.IsTracking = 1;
            """;
        using var reader = command.ExecuteReader();
        var tracked = new List<(long Id, string ProcessName, long? UserId)>();
        while (reader.Read())
        {
            tracked.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }

        reader.Close();
        foreach (var game in tracked)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.Parameters.AddWithValue("$id", game.Id);
            update.Parameters.AddWithValue("$userId", (object?)game.UserId ?? DBNull.Value);
            if (running.Contains(game.ProcessName))
            {
                update.CommandText = """
                    UPDATE GameSessions
                    SET EndedAt = LastSeenAt
                    WHERE TrackedGameId = $id AND EndedAt IS NULL
                        AND ($userId IS NULL OR GameUserId IS NOT $userId);
                    INSERT INTO GameSessions (TrackedGameId, GameUserId, StartedAt, LastSeenAt)
                    SELECT $id, $userId, $now, $now
                    WHERE $userId IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM GameSessions WHERE TrackedGameId = $id AND EndedAt IS NULL
                    );
                    UPDATE GameSessions SET LastSeenAt = $now
                    WHERE TrackedGameId = $id AND EndedAt IS NULL
                        AND GameUserId = $userId;
                    """;
                update.Parameters.AddWithValue("$now", now);
                update.ExecuteNonQuery();
            }
            else
            {
                update.CommandText = """
                    UPDATE GameSessions
                    SET EndedAt = LastSeenAt
                    WHERE TrackedGameId = $id AND EndedAt IS NULL;
                    """;
                update.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private void CloseUnfinishedSessions()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE GameSessions
            SET EndedAt = LastSeenAt
            WHERE EndedAt IS NULL;
            """;
        command.ExecuteNonQuery();
    }
}
