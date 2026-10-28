using Microsoft.Data.Sqlite;

namespace Limiter.Core;

public enum RemakeStatus
{
    Unchecked = 0,
    NotRemake = 1,
    Remake = 2,

    /// <summary>A match started in an exempt period (weekend): it does not count toward the limit.</summary>
    Exempt = 3,
}

public sealed record LolMatch(string UserSid, long GameId, DateTimeOffset StartedAt, int QueueId, RemakeStatus RemakeStatus);

public sealed record UsageBucket(DateTimeOffset Start, long Milliseconds)
{
    /// <summary>The bucket drops out of the 24-hour window completely at this time (conservative: counted from the bucket's end).</summary>
    public DateTimeOffset ReleasedAt => Start + UsageStore.BucketSize + LimitEngine.Window;
}

/// <summary>
/// SQLite-based store. Web usage is kept in per-minute buckets, LoL matches individually.
/// Every record is keyed by Windows user (SID), so Chrome profiles and LoL accounts share the counters.
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
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
    }

    private const string TrackingStartKey = "tracking_started_at";

    /// <summary>
    /// Since when the program measures usage. Recorded on the first call: with an existing database (update) the earliest
    /// recorded usage time, with an empty database (fresh install) <paramref name="now"/>.
    /// </summary>
    public DateTimeOffset EnsureTrackingStart(DateTimeOffset now)
    {
        lock (_gate)
        {
            using (var read = _db.CreateCommand())
            {
                read.CommandText = "SELECT value FROM meta WHERE key = $k;";
                read.Parameters.AddWithValue("$k", TrackingStartKey);
                if (read.ExecuteScalar() is string text && long.TryParse(text, out var ms))
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            long start = now.ToUnixTimeMilliseconds();
            using (var earliest = _db.CreateCommand())
            {
                earliest.CommandText = """
                    SELECT MIN(t) FROM (
                        SELECT MIN(bucket_start) AS t FROM web_usage
                        UNION ALL
                        SELECT MIN(started_at) FROM lol_match
                    );
                    """;
                if (earliest.ExecuteScalar() is long first) start = Math.Min(start, first);
            }
            SetTrackingStart(DateTimeOffset.FromUnixTimeMilliseconds(start));
            return DateTimeOffset.FromUnixTimeMilliseconds(start);
        }
    }

    public void SetTrackingStart(DateTimeOffset at)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($k, $v);";
            cmd.Parameters.AddWithValue("$k", TrackingStartKey);
            cmd.Parameters.AddWithValue("$v", at.ToUnixTimeMilliseconds().ToString());
            cmd.ExecuteNonQuery();
        }
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

    /// <summary>The buckets that still contain usage after <paramref name="windowStart"/>, in chronological order.</summary>
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

    /// <summary>Total web usage in the period [from, to) (by the start of the minute buckets).</summary>
    public long SumWebUsage(string userSid, WebCategory category, DateTimeOffset from, DateTimeOffset to)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT COALESCE(SUM(ms), 0) FROM web_usage
                WHERE user_sid = $sid AND category = $cat AND bucket_start >= $from AND bucket_start < $to;
                """;
            cmd.Parameters.AddWithValue("$sid", userSid);
            cmd.Parameters.AddWithValue("$cat", (int)category);
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }

    /// <summary>The number of matches counting toward the limit (not remake, not exempt) in the period [from, to).</summary>
    public int CountCountedMatches(string userSid, DateTimeOffset from, DateTimeOffset to)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM lol_match
                WHERE user_sid = $sid AND started_at >= $from AND started_at < $to
                  AND remake_status NOT IN ($remake, $exempt);
                """;
            cmd.Parameters.AddWithValue("$sid", userSid);
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$remake", (int)RemakeStatus.Remake);
            cmd.Parameters.AddWithValue("$exempt", (int)RemakeStatus.Exempt);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    /// <summary>Records a new match. The same match ID (reconnect, repeated event) is not inserted again.</summary>
    /// <returns>True if a new attempt was created.</returns>
    public bool TryRecordMatch(string userSid, long gameId, DateTimeOffset startedAt, int queueId,
        RemakeStatus initialStatus = RemakeStatus.Unchecked)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO lol_match (user_sid, game_id, started_at, queue_id, remake_status)
                VALUES ($sid, $game, $start, $queue, $status);
                """;
            cmd.Parameters.AddWithValue("$status", (int)initialStatus);
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

    /// <summary>The matches started after <paramref name="since"/>, in chronological order (remakes too).</summary>
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
