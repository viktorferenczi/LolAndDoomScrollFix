namespace Limiter.Core.Lol;

public enum MatchRecordOutcome
{
    NotApplicable,
    Recorded,
    AlreadyRecorded,
    NotPvp,
    Spectator,
    Unknown,

    /// <summary>Started in an exempt period (weekend): recorded but does not count toward the limit.</summary>
    Exempt,
}

public enum MatchmakingAction
{
    Allow,
    CancelSearch,
    DeclineReadyCheck,
}

/// <param name="Banned">The block is caused by the banned period (not by a used-up limit).</param>
public sealed record MatchmakingDecision(MatchmakingAction Action, GameKind Kind, bool Banned = false);

/// <summary>
/// Counting LoL matches and deciding whether to block searching. It keeps state only in the store,
/// so it also works correctly after a restart.
/// </summary>
public sealed class LolMatchTracker
{
    public static readonly IReadOnlySet<string> RunningPhases = new HashSet<string> { "GameStart", "InProgress", "Reconnect" };
    public static readonly IReadOnlySet<string> PreGamePhases = new HashSet<string> { "Matchmaking", "ReadyCheck", "ChampSelect" };
    public static readonly IReadOnlySet<string> PostGamePhases = new HashSet<string> { "PreEndOfGame", "WaitingForStats", "EndOfGame" };

    /// <summary>If the client does not say whether the player is a participant, a champion select within this time confirms it.</summary>
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
    /// Detecting a running match. The same match ID (reconnect, repeated event) is not a new attempt.
    /// Spectating, TFT, bot games, practice and custom games are not recorded.
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

        // A match started on a weekend is recorded as exempt, so it does not count even if it runs into Monday.
        var exempt = _engine.IsExemptNow;
        var inserted = _store.TryRecordMatch(userSid, snapshot.GameId, _clock.UtcNow, snapshot.Queue.QueueId,
            exempt ? RemakeStatus.Exempt : RemakeStatus.Unchecked);
        if (!inserted) return MatchRecordOutcome.AlreadyRecorded;
        return exempt ? MatchRecordOutcome.Exempt : MatchRecordOutcome.Recorded;
    }

    /// <summary>
    /// While searching / at a match accept: with a used-up limit, cancelling or declining in a PvP queue.
    /// For an unknown queue it lets everything through (the caller reports the problem).
    /// </summary>
    public MatchmakingDecision DecideMatchmaking(string userSid, string phase, QueueInfo queue)
    {
        var kind = LolQueueClassifier.Classify(queue);
        if (phase is not ("Matchmaking" or "ReadyCheck") || kind != GameKind.Pvp)
            return new MatchmakingDecision(MatchmakingAction.Allow, kind);
        var status = _engine.GetLolStatus(userSid);
        if (!status.Blocked)
            return new MatchmakingDecision(MatchmakingAction.Allow, kind);
        return new MatchmakingDecision(
            phase == "ReadyCheck" ? MatchmakingAction.DeclineReadyCheck : MatchmakingAction.CancelSearch, kind, status.Banned);
    }

    /// <summary>
    /// Whether the decision is still valid right before it is executed: cancelling the search can only be sent while searching,
    /// declining the accept only at a match accept. If the phase changed in the meantime
    /// (e.g. champion select, match), the call is skipped — we never touch the client during a game.
    /// </summary>
    public static bool ActionStillApplies(MatchmakingAction action, string currentPhase) => action switch
    {
        MatchmakingAction.CancelSearch => currentPhase == "Matchmaking",
        MatchmakingAction.DeclineReadyCheck => currentPhase == "ReadyCheck",
        _ => false,
    };

    /// <summary>Recording the result of a remake check. Only a confirmed remake gives the attempt back.</summary>
    public void OnRemakeResult(string userSid, long gameId, bool remade) =>
        _store.SetRemakeStatus(userSid, gameId, remade ? RemakeStatus.Remake : RemakeStatus.NotRemake);

    /// <summary>The matches whose remake state is not decided yet.</summary>
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
