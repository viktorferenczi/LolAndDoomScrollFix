using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

/// <summary>
/// Pihenőnap-jutalom. Naptár: 2026. szeptember 29. kedd, 30. szerda, október 1. csütörtök, 2. péntek,
/// 3. szombat, 4. vasárnap, 5. hétfő.
/// </summary>
public class RestDayBonusTests
{
    private const string A = TestEnv.UserA;
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static TestEnv Env(DateTimeOffset start, LimitSettings? settings = null) =>
        new(settings, start: start, restDayBonus: true);

    /// <summary>Percenként egy teljes perc használat a megadott kezdettől.</summary>
    private static void Seed(TestEnv env, WebCategory category, DateTimeOffset from, int minutes)
    {
        for (var i = 0; i < minutes; i++)
            env.Store.AddWebUsage(A, category, from + i * Minute, 60_000);
    }

    private static void Match(TestEnv env, long gameId, DateTimeOffset at) =>
        env.Store.TryRecordMatch(A, gameId, at, 420);

    [Fact]
    public void A_day_without_short_videos_gives_30_extra_minutes_next_day()
    {
        using var env = Env(At(10, 1, 12));
        Seed(env, WebCategory.FacebookFeed, At(9, 30, 10), 5); // szerdán volt hírfolyam, rövid videó nem

        var shortVideo = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.Equal(TimeSpan.FromMinutes(90), shortVideo.Limit);
        Assert.Equal(TimeSpan.FromMinutes(30), shortVideo.Bonus);

        // A két keret jutalma független: a hírfolyamnál szerdán fogyott, így ott nincs jutalom.
        var feed = env.Engine.GetWebStatus(A, WebCategory.FacebookFeed);
        Assert.Equal(TimeSpan.FromMinutes(60), feed.Limit);
        Assert.Equal(TimeSpan.Zero, feed.Bonus);
    }

    [Fact]
    public void The_bonus_does_not_stack_over_several_empty_days()
    {
        using var env = Env(At(10, 1, 12));
        Seed(env, WebCategory.ShortVideo, At(9, 25, 10), 5); // utoljára egy hete
        Assert.Equal(TimeSpan.FromMinutes(90), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        Assert.Equal(4, env.Engine.GetLolStatus(A).Limit);
    }

    [Fact]
    public void Any_usage_the_previous_day_means_no_bonus()
    {
        using var env = Env(At(10, 1, 12));
        Seed(env, WebCategory.ShortVideo, At(9, 30, 23, 59), 1);
        Match(env, 1, At(9, 30, 20));
        Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        Assert.Equal(3, env.Engine.GetLolStatus(A).Limit);
    }

    [Fact]
    public void A_day_without_matches_gives_one_extra_match_next_day()
    {
        using var env = Env(At(10, 1, 9));
        var status = env.Engine.GetLolStatus(A);
        Assert.Equal(4, status.Limit);
        Assert.Equal(1, status.Bonus);

        for (var i = 1; i <= 3; i++) Assert.Equal(MatchRecordOutcome.Recorded, env.Tracker.OnGameflow(A, TestEnv.Running(i)));
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(A, "Matchmaking", TestEnv.RankedSolo).Action);
        env.Tracker.OnGameflow(A, TestEnv.Running(4));
        Assert.Equal(MatchmakingAction.CancelSearch, env.Tracker.DecideMatchmaking(A, "Matchmaking", TestEnv.RankedSolo).Action);
    }

    [Fact]
    public void Remade_and_non_pvp_games_do_not_cancel_the_rest_day()
    {
        using var env = Env(At(10, 1, 9));
        Match(env, 1, At(9, 30, 20));
        env.Tracker.OnRemakeResult(A, 1, remade: true);
        Assert.Equal(4, env.Engine.GetLolStatus(A).Limit);
    }

    [Fact]
    public void Daily_reset_mode_uses_reset_to_reset_days()
    {
        var daily = LimitSettings.Default with { ResetMode = ResetMode.Daily, DailyResetHour = 3 };

        // Csütörtök 02:30 még a szerdai naphoz tartozik → csütörtök 03:00 után nincs jutalom.
        using (var env = Env(At(10, 1, 12), daily))
        {
            Seed(env, WebCategory.ShortVideo, At(10, 1, 2, 30), 1);
            Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        }

        // Csütörtök 03:30 már a mai nap → a szerdai nap üres volt, jár a jutalom.
        using (var env = Env(At(10, 1, 12), daily))
        {
            Seed(env, WebCategory.ShortVideo, At(10, 1, 3, 30), 1);
            var status = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
            Assert.Equal(TimeSpan.FromMinutes(90), status.Limit);
            Assert.InRange(status.Remaining.TotalMinutes, 88.9, 89.1);
        }
    }

    [Fact]
    public void With_free_weekends_monday_looks_back_to_friday()
    {
        using (var env = Env(At(10, 5, 10)))
        {
            Seed(env, WebCategory.ShortVideo, At(10, 2, 18), 10); // pénteken volt használat
            Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        }

        using (var env = Env(At(10, 5, 10)))
        {
            Seed(env, WebCategory.ShortVideo, At(10, 1, 18), 10); // csak csütörtökön → péntek üres
            Assert.Equal(TimeSpan.FromMinutes(90), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        }
    }

    [Fact]
    public void With_enforced_weekends_monday_looks_back_to_sunday()
    {
        var settings = LimitSettings.Default with { EnforceOnWeekends = true };
        using var env = Env(At(10, 5, 10), settings);
        Seed(env, WebCategory.ShortVideo, At(10, 2, 18), 10); // péntek igen, vasárnap nem
        Assert.Equal(TimeSpan.FromMinutes(90), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
    }

    [Fact]
    public void A_zero_limit_never_earns_a_bonus()
    {
        var settings = LimitSettings.Default with { ShortVideoMinutes = 0, LolPvpMatches = 0 };
        using var env = Env(At(10, 1, 12), settings);
        Assert.True(env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Blocked);
        Assert.True(env.Engine.GetLolStatus(A).Blocked);
    }

    [Fact]
    public void Rolling_mode_block_accounts_for_the_bonus_ending_at_midnight()
    {
        using var env = Env(At(10, 1, 23, 30));
        Seed(env, WebCategory.ShortVideo, At(10, 1, 22), 90); // jutalomnap: 90 perc, 22:00–23:30

        var status = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.True(status.Blocked);
        // Pénteken a keret 60 perc (csütörtökön fogyott), így 31 percnyinek kell kiesnie: péntek 22:31.
        Assert.Equal(At(10, 2, 22, 31), status.AvailableAgainAt);

        env.Clock.UtcNow = At(10, 2, 0, 5);
        Assert.True(env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Blocked);
        env.Clock.UtcNow = At(10, 2, 22, 30);
        Assert.True(env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Blocked);
        env.Clock.UtcNow = At(10, 2, 22, 31);
        Assert.False(env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Blocked);
    }

    [Fact]
    public void Rolling_mode_lol_block_accounts_for_the_bonus_ending_at_midnight()
    {
        using var env = Env(At(10, 1, 23, 30));
        for (var h = 20; h <= 23; h++) Match(env, h, At(10, 1, h)); // jutalomnap: 4 meccs

        var status = env.Engine.GetLolStatus(A);
        Assert.True(status.Blocked);
        Assert.Equal(At(10, 2, 21), status.AvailableAgainAt); // pénteken 3 a keret: 2 meccsnek kell kiesnie

        env.Clock.UtcNow = At(10, 2, 20, 30);
        Assert.True(env.Engine.GetLolStatus(A).Blocked);
        env.Clock.UtcNow = At(10, 2, 21);
        Assert.False(env.Engine.GetLolStatus(A).Blocked);
    }

    [Fact]
    public void Default_bonus_values_and_sanitizing()
    {
        Assert.Equal(1, LimitSettings.Default.LolRestDayBonusMatches);
        Assert.Equal(30, LimitSettings.Default.ShortVideoRestDayBonusMinutes);
        Assert.Equal(30, LimitSettings.Default.FacebookFeedRestDayBonusMinutes);

        var s = LimitSettings.Parse("""{ "lolRestDayBonusMatches": 50, "shortVideoRestDayBonusMinutes": -1 }""");
        Assert.Equal(10, s.LolRestDayBonusMatches);
        Assert.Equal(0, s.ShortVideoRestDayBonusMinutes);
        Assert.Equal(30, s.FacebookFeedRestDayBonusMinutes);
    }
}
