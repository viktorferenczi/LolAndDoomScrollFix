using Limiter.Core;
using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Ideiglenes adatbázis-fájl egy teszthez; az „újraindítás” új tár-példányt nyit ugyanarra a fájlra.</summary>
public sealed class TestEnv : IDisposable
{
    public const string UserA = "S-1-5-21-1000-1000-1000-1001";
    public const string UserB = "S-1-5-21-1000-1000-1000-1002";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "limiter-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Alapértelmezés: 2026. október 1., csütörtök 12:00 UTC, UTC időzónában. A pihenőnap-jutalom csak
    /// <paramref name="restDayBonus"/> esetén él (üres adatbázisnál különben minden teszt „pihenőnap” után indulna).
    /// </summary>
    public TestEnv(LimitSettings? settings = null, TimeZoneInfo? zone = null, DateTimeOffset? start = null,
        bool restDayBonus = false, DateTimeOffset? trackingStart = null)
    {
        Directory.CreateDirectory(_dir);
        Settings = settings ?? LimitSettings.Default;
        if (!restDayBonus) Settings = Settings.WithoutRestDayBonus();
        Zone = zone ?? TimeZoneInfo.Utc;
        Clock = new FakeClock(start ?? new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        // A jutalom teszteiben a program „régóta mér”, kivéve ha a teszt külön megadja a mérés kezdetét.
        _initialTrackingStart = trackingStart ?? (restDayBonus ? Clock.UtcNow - TimeSpan.FromDays(30) : null);
        Open();
    }

    private DateTimeOffset? _initialTrackingStart;

    public TimeZoneInfo Zone { get; }

    public FakeClock Clock { get; }
    public LimitSettings Settings { get; set; }
    public UsageStore Store { get; private set; } = null!;
    public LimitEngine Engine { get; private set; } = null!;
    public LolMatchTracker Tracker { get; private set; } = null!;

    public string DbPath => Path.Combine(_dir, "limiter.db");

    /// <summary>A szolgáltatás újraindításának szimulálása.</summary>
    public void Restart()
    {
        Store.Dispose();
        Open();
    }

    private void Open()
    {
        Store = new UsageStore(DbPath);
        if (_initialTrackingStart is { } tracking)
        {
            Store.SetTrackingStart(tracking);
            _initialTrackingStart = null; // újraindításkor a tárolt érték marad
        }
        Engine = new LimitEngine(Store, () => Settings, Clock, Zone);
        Tracker = new LolMatchTracker(Store, Engine, Clock);
    }

    public static QueueInfo RankedSolo => new(420, "CLASSIC", "PvP", "RANKED_SOLO_5x5", false);
    public static QueueInfo Aram => new(450, "ARAM", "PvP", "ARAM_UNRANKED_5x5", false);
    public static QueueInfo TftNormal => new(1090, "TFT", "PvP", "NORMAL_TFT", false);
    public static QueueInfo BotIntermediate => new(890, "CLASSIC", "VersusAi", "BOT", false);
    public static QueueInfo Practice => new(0, "PRACTICETOOL", "Custom", "", true);
    public static QueueInfo Custom => new(-1, "CLASSIC", "Custom", "", true);

    public static GameflowSnapshot Running(long gameId, QueueInfo? queue = null, bool? participant = true, string phase = "InProgress") =>
        new(phase, gameId, queue ?? RankedSolo, participant);

    public void Dispose()
    {
        Store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
