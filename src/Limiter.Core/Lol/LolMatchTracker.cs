namespace Limiter.Core.Lol;

public enum MatchRecordOutcome
{
    NotApplicable,
    Recorded,
    AlreadyRecorded,
    NotPvp,
    Spectator,
    Unknown,
}

public enum MatchmakingAction
{
    Allow,
    CancelSearch,
    DeclineReadyCheck,
}

public sealed record MatchmakingDecision(MatchmakingAction Action, GameKind Kind);

/// <summary>
/// A LoL-meccsek számlálása és a keresés tiltásának döntése. Állapotot csak a tárban tart,
/// így újraindítás után is helyesen működik.
/// </summary>
public sealed class LolMatchTracker
{
    public static readonly IReadOnlySet<string> RunningPhases = new HashSet<string> { "GameStart", "InProgress", "Reconnect" };
    public static readonly IReadOnlySet<string> PreGamePhases = new HashSet<string> { "Matchmaking", "ReadyCheck", "ChampSelect" };
    public static readonly IReadOnlySet<string> PostGamePhases = new HashSet<string> { "PreEndOfGame", "WaitingForStats", "EndOfGame" };

    /// <summary>Ha a kliens nem mondja meg, résztvevő-e a játékos, ennyi időn belüli hősválasztás igazolja.</summary>
    private static readonly TimeSpan PreGameEvidenceWindow = TimeSpan.FromHours(2);

    private readonly UsageStore _store;
    private readonly LimitEngine _engine;
    private readonly IClock _clock;
    private readonly Dictionary<string, DateTimeOffset> _lastPreGame = [];
    private readonly Lock _gate = new();

    public LolMatchTracker(UsageStore store, LimitEngine engine, IClock clock)
    {
        _store = store;
        _engine = engine;
        _clock = clock;
    }

    public void NotePreGame(string userSid)
    {
        lock (_gate) _lastPreGame[userSid] = _clock.UtcNow;
    }

    /// <summary>
    /// Futó meccs észlelése. Ugyanaz a meccsazonosító (újracsatlakozás, ismételt esemény) nem számít új alkalomnak.
    /// Néző mód, TFT, botmeccs, gyakorlás és egyéni játék nem kerül rögzítésre.
    /// </summary>
    public MatchRecordOutcome OnGameflow(string userSid, GameflowSnapshot snapshot)
    {
        if (PreGamePhases.Contains(snapshot.Phase))
        {
            NotePreGame(userSid);
            return MatchRecordOutcome.NotApplicable;
        }
        if (!RunningPhases.Contains(snapshot.Phase)) return MatchRecordOutcome.NotApplicable;
        if (snapshot.GameId <= 0) return MatchRecordOutcome.Unknown;

        switch (LolQueueClassifier.Classify(snapshot.Queue))
        {
            case GameKind.NotPvp: return MatchRecordOutcome.NotPvp;
            case GameKind.Unknown: return MatchRecordOutcome.Unknown;
        }

        if (snapshot.LocalPlayerIsParticipant == false) return MatchRecordOutcome.Spectator;
        if (snapshot.LocalPlayerIsParticipant is null && !HasRecentPreGame(userSid)) return MatchRecordOutcome.Unknown;

        return _store.TryRecordMatch(userSid, snapshot.GameId, _clock.UtcNow, snapshot.Queue.QueueId)
            ? MatchRecordOutcome.Recorded
            : MatchRecordOutcome.AlreadyRecorded;
    }

    /// <summary>
    /// Keresés / meccselfogadás közben: elfogyott keretnél PvP-sorban megszakítás vagy elutasítás.
    /// Ismeretlen sor esetén enged (a hívó jelzi a hibát).
    /// </summary>
    public MatchmakingDecision DecideMatchmaking(string userSid, string phase, QueueInfo queue)
    {
        var kind = LolQueueClassifier.Classify(queue);
        if (phase is not ("Matchmaking" or "ReadyCheck") || kind != GameKind.Pvp)
            return new MatchmakingDecision(MatchmakingAction.Allow, kind);
        if (!_engine.GetLolStatus(userSid).Blocked)
            return new MatchmakingDecision(MatchmakingAction.Allow, kind);
        return new MatchmakingDecision(
            phase == "ReadyCheck" ? MatchmakingAction.DeclineReadyCheck : MatchmakingAction.CancelSearch, kind);
    }

    /// <summary>Remake-ellenőrzés eredményének rögzítése. Csak igazolt remake adja vissza az alkalmat.</summary>
    public void OnRemakeResult(string userSid, long gameId, bool remade) =>
        _store.SetRemakeStatus(userSid, gameId, remade ? RemakeStatus.Remake : RemakeStatus.NotRemake);

    /// <summary>Azok a meccsek, amelyeknek remake-állapota még nincs eldöntve.</summary>
    public IReadOnlyList<LolMatch> PendingRemakeChecks(string userSid) =>
        _store.GetMatchesSince(userSid, _clock.UtcNow - LimitEngine.Window)
            .Where(m => m.RemakeStatus == RemakeStatus.Unchecked)
            .ToList();

    private bool HasRecentPreGame(string userSid)
    {
        lock (_gate)
            return _lastPreGame.TryGetValue(userSid, out var at) && _clock.UtcNow - at < PreGameEvidenceWindow;
    }
}
