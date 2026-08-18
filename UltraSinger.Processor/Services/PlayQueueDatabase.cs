using Microsoft.Data.Sqlite;
using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Raw SQLite access for the play queue, following the same hand-written-SQL approach as
/// <see cref="SongDatabase"/> — kept in the same database file since it's the same simple
/// single-table style of access.
/// </summary>
public class PlayQueueDatabase(EnvironmentalValuesService environmentalValues, ILogger<PlayQueueDatabase> logger)
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
            CREATE TABLE IF NOT EXISTS PlayQueue (
                Id        TEXT PRIMARY KEY,
                Title     TEXT NOT NULL,
                Artist    TEXT NOT NULL DEFAULT '',
                Source    INTEGER NOT NULL,
                ExtraInfo TEXT,
                FilePath  TEXT,
                QueuedAt  TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                IsSung    INTEGER NOT NULL DEFAULT 0,
                SungAt    TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_PlayQueue_SortOrder ON PlayQueue (SortOrder);
            """;
        command.ExecuteNonQuery();

        logger.LogInformation("Play queue database ready at {Path}", environmentalValues.SongDatabasePath);
    }

    public List<PlayQueueItemRecord> LoadAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Title, Artist, Source, ExtraInfo, FilePath, QueuedAt, SortOrder, IsSung, SungAt
            FROM PlayQueue ORDER BY SortOrder ASC
            """;

        var records = new List<PlayQueueItemRecord>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            records.Add(new PlayQueueItemRecord
            {
                Id = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                Artist = reader.GetString(2),
                Source = (SongSource)reader.GetInt32(3),
                ExtraInfo = reader.IsDBNull(4) ? null : reader.GetString(4),
                FilePath = reader.IsDBNull(5) ? null : reader.GetString(5),
                QueuedAt = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                SortOrder = reader.GetInt32(7),
                IsSung = reader.GetInt32(8) != 0,
                SungAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind)
            });
        }

        return records;
    }

    public void Upsert(PlayQueueItemRecord item)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO PlayQueue (Id, Title, Artist, Source, ExtraInfo, FilePath, QueuedAt, SortOrder, IsSung, SungAt)
            VALUES ($id, $title, $artist, $source, $extraInfo, $filePath, $queuedAt, $sortOrder, $isSung, $sungAt)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title, Artist = excluded.Artist, Source = excluded.Source,
                ExtraInfo = excluded.ExtraInfo, FilePath = excluded.FilePath, QueuedAt = excluded.QueuedAt,
                SortOrder = excluded.SortOrder, IsSung = excluded.IsSung, SungAt = excluded.SungAt
            """;

        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$artist", item.Artist);
        command.Parameters.AddWithValue("$source", (int)item.Source);
        command.Parameters.AddWithValue("$extraInfo", (object?)item.ExtraInfo ?? DBNull.Value);
        command.Parameters.AddWithValue("$filePath", (object?)item.FilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$queuedAt", item.QueuedAt.ToString("O"));
        command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
        command.Parameters.AddWithValue("$isSung", item.IsSung ? 1 : 0);
        command.Parameters.AddWithValue("$sungAt", (object?)item.SungAt?.ToString("O") ?? DBNull.Value);

        command.ExecuteNonQuery();
    }

    /// <summary>Updates the SortOrder of many rows in one transaction, used when reordering.</summary>
    public void SaveOrder(IReadOnlyCollection<(Guid Id, int SortOrder)> ordering)
    {
        if (ordering.Count == 0)
        {
            return;
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE PlayQueue SET SortOrder = $sortOrder WHERE Id = $id";
        var idParam = command.Parameters.Add("$id", SqliteType.Text);
        var sortOrderParam = command.Parameters.Add("$sortOrder", SqliteType.Integer);

        foreach (var (id, sortOrder) in ordering)
        {
            idParam.Value = id.ToString();
            sortOrderParam.Value = sortOrder;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Delete(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PlayQueue WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    public void DeleteAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PlayQueue";
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();

        return connection;
    }
}
