using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

/// <summary>
/// A program soha nem nyúlhat a klienshez játék közben: a megszakítás/elutasítás csak keresés és meccselfogadás
/// közben mehet ki, és a lekérdezés játék közben ritkább.
/// </summary>
public class GameSafetyTests
{
    [Theory]
    [InlineData(MatchmakingAction.CancelSearch, "Matchmaking", true)]
    [InlineData(MatchmakingAction.CancelSearch, "ReadyCheck", false)]
    [InlineData(MatchmakingAction.DeclineReadyCheck, "ReadyCheck", true)]
    [InlineData(MatchmakingAction.DeclineReadyCheck, "Matchmaking", false)]
    [InlineData(MatchmakingAction.Allow, "Matchmaking", false)]
    [InlineData(MatchmakingAction.Allow, "ReadyCheck", false)]
    public void Search_and_ready_check_actions_only_apply_in_their_own_phase(MatchmakingAction action, string phase, bool expected) =>
        Assert.Equal(expected, LolMatchTracker.ActionStillApplies(action, phase));

    [Theory]
    [InlineData("None")]
    [InlineData("Lobby")]
    [InlineData("ChampSelect")]
    [InlineData("GameStart")]
    [InlineData("InProgress")]
    [InlineData("Reconnect")]
    [InlineData("PreEndOfGame")]
    [InlineData("WaitingForStats")]
    [InlineData("EndOfGame")]
    [InlineData("")]
    public void No_cancel_or_decline_is_ever_sent_outside_search_and_ready_check(string phase)
    {
        Assert.False(LolMatchTracker.ActionStillApplies(MatchmakingAction.CancelSearch, phase));
        Assert.False(LolMatchTracker.ActionStillApplies(MatchmakingAction.DeclineReadyCheck, phase));
    }

    [Theory]
    [InlineData("GameStart")]
    [InlineData("InProgress")]
    [InlineData("Reconnect")]
    public void Polling_is_slow_while_a_game_is_running(string phase) =>
        Assert.Equal(LcuPolling.InGame, LcuPolling.IntervalFor(phase));

    [Theory]
    [InlineData(null)]
    [InlineData("None")]
    [InlineData("Lobby")]
    [InlineData("Matchmaking")]
    [InlineData("ReadyCheck")]
    [InlineData("ChampSelect")]
    [InlineData("EndOfGame")]
    public void Polling_is_fast_outside_a_running_game(string? phase)
    {
        Assert.Equal(LcuPolling.Normal, LcuPolling.IntervalFor(phase));
        Assert.True(LcuPolling.InGame > LcuPolling.Normal);
    }

    [Fact]
    public void Blocked_player_in_a_running_game_gets_no_state_changing_action()
    {
        using var env = new TestEnv();
        for (var i = 1; i <= 3; i++) env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(i));
        Assert.True(env.Engine.GetLolStatus(TestEnv.UserA).Blocked);

        // Elfogyott keret mellett is: játék közben nincs Cancel/Decline döntés.
        foreach (var phase in new[] { "GameStart", "InProgress", "Reconnect", "ChampSelect", "Lobby" })
            Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(TestEnv.UserA, phase, TestEnv.RankedSolo).Action);
    }
}
