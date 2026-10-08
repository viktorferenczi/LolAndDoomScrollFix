using Microsoft.Data.Sqlite;

namespace Limiter.Core;

public enum RemakeStatus
{
    Unchecked = 0,
    NotRemake = 1,
    Remake = 2,
}

public sealed record LolMatch(string UserSid, long GameId, DateTimeOffset StartedAt, int QueueId, RemakeStatus RemakeStatus);

public sealed record UsageBucket(DateTimeOffset Start, long Milliseconds)
{
    /// <summary>A vödör ekkor esik ki teljesen a 24 órás ablakból (konzervatív: a vödör végétől számolva).</summary>
    public DateTimeOffset ReleasedAt => Start + UsageStore.BucketSize + LimitEngine.Window;
}

/// <summary>
/// SQLite-alapú tár. A webes használat percenkénti vödrökben, a LoL-meccsek egyenként tárolódnak.
/// Minden rekord Windows-felhasználó (SID) szerint van kulcsolva, így a Chrome-profilok és a LoL-fiókok közösek.
/// </summary>
public sealed class UsageStore : IDisposable
{
    public static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(1);
    private static readonly long BucketMs = (long)BucketSize.TotalMilliseconds;

    private readonly SqliteConnection _db;
    private readonly Lock _gate = new();

    public UsageStore(string databasePath)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        _db = new SqliteConnection(cs);
        _db.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=FULL;");
        Execute("""
            CREATE TABLE IF NOT EXISTS web_usage (
                user_sid     TEXT    NOT NULL,
                category     INTEGER NOT NULL,
                bucket_start INTEGER NOT NULL,
                ms           INTEGER NOT NULL,
                PRIMARY KEY (user_sid, category, bucket_start)
            );
            CREATE TABLE IF NOT EXISTS lol_match (
                user_sid      TEXT    NOT NULL,
                game_id       INTEGER NOT NULL,
                started_at    INTEGER NOT NULL,
                queue_id      INTEGER NOT NULL,
                remake_status INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (user_sid, game_id)
            );
            """);
    }

    public void AddWebUsage(string userSid, WebCategory category, DateTimeOffset at, long milliseconds)
    {
        if (milliseconds <= 0) return;
        var bucket = Math.DivRem(at.ToUnixTimeMilliseconds(), BucketMs).Quotient * BucketMs;
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO web_usage (user_sid, category, bucket_start, ms) VALUES ($sid, $cat, $bucket, $ms)
                ON CONFLICT (user_sid, category, bucket_start) DO UPDATE SET ms = ms + excluded.ms;
                """;
            cmd.Parameters.AddWithValue("$sid", userSid);
            cmd.Parameters.AddWithValue("$cat", (int)category);
            cmd.Parameters.AddWithValue("$bucket", bucket);
            cmd.Parameters.AddWithValue("$ms", milliseconds);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Azok a vödrök, amelyek <paramref name="windowStart"/> után még tartalmaznak használatot, időrendben.</summary>
    public IReadOnlyList<UsageBucket> GetWebBuckets(string userSid, WebCategory category, DateTimeOffset windowStart)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT bucket_start, ms FROM web_usage
                WHERE user_sid = $sid AND category = $cat AND bucket_start + $bucketMs > $start
                ORDER BY bucket_start;
                """;
            cmd.Parameters.AddWithValue("$sid", userSid);
            cmd.Parameters.AddWithValue("$cat", (int)category);
            cmd.Parameters.AddWithValue("$bucketMs", BucketMs);
            cmd.Parameters.AddWithValue("$start", windowStart.ToUnixTimeMilliseconds());
            using var reader = cmd.ExecuteReader();
            var result = new List<UsageBucket>();
            while (reader.Read())
                result.Add(new UsageBucket(DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)), reader.GetInt64(1)));
            return result;
        }
    }

    /// <summary>Új meccs rögzítése. Ugyanaz a meccsazonosító (újracsatlakozás, ismételt esemény) nem kerül be újra.</summary>
    /// <returns>Igaz, ha új alkalom keletkezett.</returns>
    public bool TryRecordMatch(string userSid, long gameId, DateTimeOffset startedAt, int queueId)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO lol_match (user_sid, game_id, started_at, queue_id, remake_status)
                VALUES ($sid, $game, $start, $queue, 0);
                """;
            cmd.Parameters.AddWithValue("$sid", userSid);
            cmd.Parameters.AddWithValue("$game", gameId);
            cmd.Parameters.AddWithValue("$start", startedAt.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$queue", queueId);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    public void SetRemakeStatus(string userSid, long gameId, RemakeStatus status)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE lol_match SET remake_status = $status WHERE user_sid = $sid AND game_id = $game;";
            cmd.Parameters.AddWithValue("$status", (int)status);
            cmd.Parameters.AddWithValue("$sid", userSid);
            cmd.Parameters.AddWithValue("$game", gameId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>A <paramref name="since"/> után indult meccsek időrendben (a remake-ek is).</summary>
    public IReadOnlyList<LolMatch> GetMatchesSince(string userSid, DateTimeOffset since)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT game_id, started_at, queue_id, remake_status FROM lol_match
                WHERE user_sid = $sid AND started_at > $since
                ORDER BY started_at;
                """;
            cmd.Parameters.AddWithValue("$sid", userSid);
            cmd.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
            using var reader = cmd.ExecuteReader();
            var result = new List<LolMatch>();
            while (reader.Read())
            {
                result.Add(new LolMatch(userSid, reader.GetInt64(0),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    reader.GetInt32(2), (RemakeStatus)reader.GetInt32(3)));
            }
            return result;
        }
    }

    public void Prune(DateTimeOffset olderThan)
    {
        var cutoff = olderThan.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                DELETE FROM web_usage WHERE bucket_start < $cutoff;
                DELETE FROM lol_match WHERE started_at < $cutoff;
                """;
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            cmd.ExecuteNonQuery();
        }
    }

    private void Execute(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_gate) _db.Dispose();
    }
}
