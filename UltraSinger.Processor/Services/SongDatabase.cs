using Microsoft.Data.Sqlite;
using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Raw SQLite access for song records. Hand-written SQL rather than an ORM — this is a
/// single table and the project has no other data access to be consistent with.
///
/// Dates are stored as round-trip ("O") strings so they survive with their offset intact.
/// </summary>
public class SongDatabase(EnvironmentalValuesService environmentalValues, ILogger<SongDatabase> logger)
{
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = environmentalValues.SongDatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    public void Initialise()
    {
        Directory.CreateDirectory(environmentalValues.DatabaseDirectory);

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Songs (
                Id                TEXT PRIMARY KEY,
                Url               TEXT NOT NULL,
                Title             TEXT,
                JobId             TEXT,
                State             INTEGER NOT NULL,
                CreatedAt         TEXT NOT NULL,
                BeganProcessingAt TEXT,
                CompletedAt       TEXT,
                UltraStarTxtPath  TEXT,
                Log               TEXT NOT NULL DEFAULT '',
                Errors            TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS IX_Songs_CreatedAt ON Songs (CreatedAt);
            """;
        command.ExecuteNonQuery();

        // Additive migration: CREATE TABLE IF NOT EXISTS above is a no-op against a
        // songs.db that already existed before these columns did.
        EnsureColumn(connection, "Songs", "BundlePath", "TEXT");
        EnsureColumn(connection, "Songs", "FetchedAt", "TEXT");
        EnsureColumn(connection, "Songs", "Source", "INTEGER DEFAULT 0");
        EnsureColumn(connection, "Songs", "UsdbSongId", "INTEGER");
        EnsureColumn(connection, "Songs", "UltraStarTxt", "TEXT");

        logger.LogInformation("Song database ready at {Path}", environmentalValues.SongDatabasePath);
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string type)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();

        while (reader.Read())
        {
            // Column name is the second field returned by table_info.
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();
    }

    public List<SongRecord> LoadAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Url, Title, JobId, State, CreatedAt, BeganProcessingAt,
                   CompletedAt, UltraStarTxtPath, BundlePath, FetchedAt,
                   Source, UsdbSongId, UltraStarTxt, Log, Errors
            FROM Songs ORDER BY CreatedAt DESC
            """;

        var records = new List<SongRecord>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            records.Add(SongRecord.FromSnapshot(new SongSnapshot(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                (SongState)reader.GetInt32(4),
                DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                ReadNullableDate(reader, 6),
                ReadNullableDate(reader, 7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                ReadNullableDate(reader, 10),
                reader.IsDBNull(11) ? SongSource.YouTube : (SongSource)reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetString(14),
                reader.GetString(15))));
        }

        return records;
    }

    /// <summary>Upserts a batch inside one transaction — the flusher writes in bulk.</summary>
    public void Save(IReadOnlyCollection<SongSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Songs (Id, Url, Title, JobId, State, CreatedAt, BeganProcessingAt,
                               CompletedAt, UltraStarTxtPath, BundlePath, FetchedAt,
                               Source, UsdbSongId, UltraStarTxt, Log, Errors)
            VALUES ($id, $url, $title, $jobId, $state, $createdAt, $beganAt,
                    $completedAt, $txtPath, $bundlePath, $fetchedAt,
                    $source, $usdbSongId, $ultraStarTxt, $log, $errors)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title, JobId = excluded.JobId, State = excluded.State,
                BeganProcessingAt = excluded.BeganProcessingAt, CompletedAt = excluded.CompletedAt,
                UltraStarTxtPath = excluded.UltraStarTxtPath, BundlePath = excluded.BundlePath,
                FetchedAt = excluded.FetchedAt, Source = excluded.Source,
                UsdbSongId = excluded.UsdbSongId, UltraStarTxt = excluded.UltraStarTxt,
                Log = excluded.Log, Errors = excluded.Errors
            """;

        var parameters = new[]
        {
            "$id", "$url", "$title", "$jobId", "$state", "$createdAt",
            "$beganAt", "$completedAt", "$txtPath", "$bundlePath", "$fetchedAt",
            "$source", "$usdbSongId", "$ultraStarTxt", "$log", "$errors"
        }.ToDictionary(name => name, name => command.Parameters.Add(name, SqliteType.Text));

        foreach (var snapshot in snapshots)
        {
            parameters["$id"].Value = snapshot.Id.ToString();
            parameters["$url"].Value = snapshot.Url;
            parameters["$title"].Value = (object?)snapshot.Title ?? DBNull.Value;
            parameters["$jobId"].Value = (object?)snapshot.JobId ?? DBNull.Value;
            parameters["$state"].Value = ((int)snapshot.State).ToString();
            parameters["$createdAt"].Value = snapshot.CreatedAt.ToString("O");
            parameters["$beganAt"].Value = (object?)snapshot.BeganProcessingAt?.ToString("O") ?? DBNull.Value;
            parameters["$completedAt"].Value = (object?)snapshot.CompletedAt?.ToString("O") ?? DBNull.Value;
            parameters["$txtPath"].Value = (object?)snapshot.UltraStarTxtPath ?? DBNull.Value;
            parameters["$bundlePath"].Value = (object?)snapshot.BundlePath ?? DBNull.Value;
            parameters["$fetchedAt"].Value = (object?)snapshot.FetchedAt?.ToString("O") ?? DBNull.Value;
            parameters["$source"].Value = ((int)snapshot.Source).ToString();
            parameters["$usdbSongId"].Value = (object?)snapshot.UsdbSongId?.ToString() ?? DBNull.Value;
            parameters["$ultraStarTxt"].Value = (object?)snapshot.UltraStarTxt ?? DBNull.Value;
            parameters["$log"].Value = snapshot.Log;
            parameters["$errors"].Value = snapshot.Errors;

            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Delete(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Songs WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        // WAL keeps the once-a-second flusher from blocking reads.
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static DateTime? ReadNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);
}
