using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

public class LolLimitTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void Three_pvp_matches_block_new_search_and_rolling_window_releases_them_one_by_one()
    {
        using var env = new TestEnv();
        var t0 = env.Clock.UtcNow;

        for (var i = 1; i <= 3; i++)
        {
            Assert.Equal(MatchRecordOutcome.Recorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(i)));
            env.Clock.Advance(Hour);
        }

        var status = env.Engine.GetLolStatus(TestEnv.UserA);
        Assert.Equal(3, status.Used);
        Assert.True(status.Blocked);
        Assert.Equal(t0 + LimitEngine.Window, status.NextReleaseAt);
        Assert.Equal(t0 + LimitEngine.Window, status.AvailableAgainAt);
        Assert.Equal(MatchmakingAction.CancelSearch, env.Tracker.DecideMatchmaking(TestEnv.UserA, "Matchmaking", TestEnv.RankedSolo).Action);
        Assert.Equal(MatchmakingAction.DeclineReadyCheck, env.Tracker.DecideMatchmaking(TestEnv.UserA, "ReadyCheck", TestEnv.RankedSolo).Action);

        // Egy másodperccel a 24 órás határ előtt még tiltott.
        env.Clock.UtcNow = t0 + LimitEngine.Window - TimeSpan.FromSeconds(1);
        Assert.True(env.Engine.GetLolStatus(TestEnv.UserA).Blocked);

        // Pontosan 24 órával az első meccs után felszabadul egy alkalom — nincs éjféli nullázás.
        env.Clock.UtcNow = t0 + LimitEngine.Window + TimeSpan.FromSeconds(1);
        status = env.Engine.GetLolStatus(TestEnv.UserA);
        Assert.False(status.Blocked);
        Assert.Equal(1, status.Remaining);
        Assert.Equal(t0 + Hour + LimitEngine.Window, status.NextReleaseAt);
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(TestEnv.UserA, "Matchmaking", TestEnv.RankedSolo).Action);
    }

    [Fact]
    public void Reconnect_and_repeated_events_for_same_game_do_not_count_again()
    {
        using var env = new TestEnv();
        Assert.Equal(MatchRecordOutcome.Recorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(77, phase: "GameStart")));
        Assert.Equal(MatchRecordOutcome.AlreadyRecorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(77)));
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(MatchRecordOutcome.AlreadyRecorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(77, phase: "Reconnect")));
        Assert.Equal(MatchRecordOutcome.AlreadyRecorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(77)));

        Assert.Equal(1, env.Engine.GetLolStatus(TestEnv.UserA).Used);
    }

    [Fact]
    public void Third_game_in_progress_and_its_reconnect_are_allowed()
    {
        using var env = new TestEnv();
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(1));
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(2));

        // A harmadik keresés még engedett.
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(TestEnv.UserA, "Matchmaking", TestEnv.RankedSolo).Action);
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(TestEnv.UserA, "ReadyCheck", TestEnv.RankedSolo).Action);
        Assert.Equal(MatchRecordOutcome.Recorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(3)));

        // Újracsatlakozás a harmadikhoz: nem új alkalom, és nincs keresés, amit tiltani kellene.
        Assert.Equal(MatchRecordOutcome.AlreadyRecorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(3, phase: "Reconnect")));
        Assert.Equal(3, env.Engine.GetLolStatus(TestEnv.UserA).Used);
    }

    [Fact]
    public void Verified_remake_gives_back_the_slot_but_short_game_alone_does_not()
    {
        using var env = new TestEnv();
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(1));
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(2));
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(3));
        Assert.True(env.Engine.GetLolStatus(TestEnv.UserA).Blocked);

        // Rövid, de nem remake meccs: marad a számlálóban.
        env.Tracker.OnRemakeResult(TestEnv.UserA, 2, remade: false);
        Assert.True(env.Engine.GetLolStatus(TestEnv.UserA).Blocked);

        env.Tracker.OnRemakeResult(TestEnv.UserA, 3, remade: true);
        var status = env.Engine.GetLolStatus(TestEnv.UserA);
        Assert.False(status.Blocked);
        Assert.Equal(2, status.Used);
        Assert.Single(env.Tracker.PendingRemakeChecks(TestEnv.UserA));
    }

    [Fact]
    public void Remade_game_reported_again_is_not_counted_again()
    {
        using var env = new TestEnv();
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(9));
        env.Tracker.OnRemakeResult(TestEnv.UserA, 9, remade: true);
        Assert.Equal(MatchRecordOutcome.AlreadyRecorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(9)));
        Assert.Equal(0, env.Engine.GetLolStatus(TestEnv.UserA).Used);
    }

    [Theory]
    [MemberData(nameof(NonPvpQueues))]
    public void Non_pvp_modes_never_count_and_stay_available_after_the_limit(QueueInfo queue)
    {
        using var env = new TestEnv();
        for (var i = 1; i <= 3; i++) env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(i));
        Assert.True(env.Engine.GetLolStatus(TestEnv.UserA).Blocked);

        Assert.Equal(MatchRecordOutcome.NotPvp, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(100, queue)));
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(TestEnv.UserA, "Matchmaking", queue).Action);
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(TestEnv.UserA, "ReadyCheck", queue).Action);
        Assert.Equal(3, env.Engine.GetLolStatus(TestEnv.UserA).Used);
    }

    public static TheoryData<QueueInfo> NonPvpQueues() =>
        [TestEnv.TftNormal, TestEnv.BotIntermediate, TestEnv.Practice, TestEnv.Custom, new QueueInfo(2000, "TUTORIAL_MODULE_1", null, "TUTORIAL_MODULE_1", false)];

    [Fact]
    public void Spectating_does_not_count()
    {
        using var env = new TestEnv();
        Assert.Equal(MatchRecordOutcome.Spectator, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(5, participant: false)));
        Assert.Equal(0, env.Engine.GetLolStatus(TestEnv.UserA).Used);
    }

    [Fact]
    public void Unknown_participation_counts_only_with_recent_champ_select()
    {
        using var env = new TestEnv();
        Assert.Equal(MatchRecordOutcome.Unknown, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(5, participant: null)));

        env.Tracker.OnGameflow(TestEnv.UserA, new GameflowSnapshot("ChampSelect", 0, TestEnv.RankedSolo, null));
        env.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(MatchRecordOutcome.Recorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(5, participant: null)));
    }

    [Fact]
    public void Unknown_queue_fails_open()
    {
        using var env = new TestEnv();
        for (var i = 1; i <= 3; i++) env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(i));
        var decision = env.Tracker.DecideMatchmaking(TestEnv.UserA, "Matchmaking", QueueInfo.Empty);
        Assert.Equal(MatchmakingAction.Allow, decision.Action);
        Assert.Equal(GameKind.Unknown, decision.Kind);
    }

    [Fact]
    public void Counters_survive_service_restart()
    {
        using var env = new TestEnv();
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(1));
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(2));
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(3));

        env.Restart();

        Assert.True(env.Engine.GetLolStatus(TestEnv.UserA).Blocked);
        Assert.Equal(MatchRecordOutcome.AlreadyRecorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(3, phase: "Reconnect")));
    }

    [Fact]
    public void Counters_are_per_windows_user_but_shared_across_lol_accounts()
    {
        using var env = new TestEnv();
        // Két különböző LoL-fiók ugyanazon a Windows-felhasználón: a SID azonos, a keret közös.
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(1, TestEnv.RankedSolo));
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(2, TestEnv.Aram));
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(3, TestEnv.RankedSolo));

        Assert.True(env.Engine.GetLolStatus(TestEnv.UserA).Blocked);
        Assert.Equal(0, env.Engine.GetLolStatus(TestEnv.UserB).Used);
    }

    [Fact]
    public void Changed_limit_applies_immediately()
    {
        using var env = new TestEnv();
        env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(1));
        env.Settings = env.Settings with { LolPvpMatches = 1 };
        Assert.True(env.Engine.GetLolStatus(TestEnv.UserA).Blocked);
    }
}
