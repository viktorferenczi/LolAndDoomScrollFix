using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

/// <summary>
/// Rest-day bonus. Calendar: Tuesday 29 September 2026, Wednesday the 30th, Thursday 1 October, Friday the 2nd,
/// Saturday the 3rd, Sunday the 4th, Monday the 5th.
/// </summary>
public class RestDayBonusTests
{
    private const string A = TestEnv.UserA;
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static TestEnv Env(DateTimeOffset start, LimitSettings? settings = null) =>
        new(settings, start: start, restDayBonus: true);

    /// <summary>One full minute of usage per minute from the given start.</summary>
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
        Seed(env, WebCategory.FacebookFeed, At(9, 30, 10), 5); // there was feed on Wednesday, no short video

        var shortVideo = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.Equal(TimeSpan.FromMinutes(90), shortVideo.Limit);
        Assert.Equal(TimeSpan.FromMinutes(30), shortVideo.Bonus);

        // The bonuses of the two limits are independent: the feed was used on Wednesday, so there is no bonus there.
        var feed = env.Engine.GetWebStatus(A, WebCategory.FacebookFeed);
        Assert.Equal(TimeSpan.FromMinutes(60), feed.Limit);
        Assert.Equal(TimeSpan.Zero, feed.Bonus);
    }

    [Fact]
    public void The_bonus_does_not_stack_over_several_empty_days()
    {
        using var env = Env(At(10, 1, 12));
        Seed(env, WebCategory.ShortVideo, At(9, 25, 10), 5); // last a week ago
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

        // Thursday 02:30 still belongs to the Wednesday day → no bonus after Thursday 03:00.
        using (var env = Env(At(10, 1, 12), daily))
        {
            Seed(env, WebCategory.ShortVideo, At(10, 1, 2, 30), 1);
            Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        }

        // Thursday 03:30 is already today → the Wednesday day was empty, the bonus is due.
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
            Seed(env, WebCategory.ShortVideo, At(10, 2, 18), 10); // there was usage on Friday
            Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        }

        using (var env = Env(At(10, 5, 10)))
        {
            Seed(env, WebCategory.ShortVideo, At(10, 1, 18), 10); // only on Thursday → Friday is empty
            Assert.Equal(TimeSpan.FromMinutes(90), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        }
    }

    [Fact]
    public void With_enforced_weekends_monday_looks_back_to_sunday()
    {
        var settings = LimitSettings.Default with { EnforceOnWeekends = true };
        using var env = Env(At(10, 5, 10), settings);
        Seed(env, WebCategory.ShortVideo, At(10, 2, 18), 10); // Friday yes, Sunday no
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
        // On Friday the limit is 60 minutes (it was used on Thursday), so 31 minutes must drop out: Friday 22:31.
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
        Assert.Equal(At(10, 2, 21), status.AvailableAgainAt); // on Friday the limit is 3: 2 matches must drop out

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

    // --- Fresh install: the day before the program started is not an "empty day" ---

    [Fact]
    public void A_fresh_install_gets_no_bonus_for_the_day_before_it_existed()
    {
        // Installed on Thursday at 12:00 with an empty database; the limits are the base ones: no bonus.
        using var env = new TestEnv(start: At(10, 1, 12), restDayBonus: true, trackingStart: At(10, 1, 12));
        Assert.Equal(3, env.Engine.GetLolStatus(A).Limit);
        Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.FacebookFeed).Limit);
    }

    [Fact]
    public void A_fresh_install_with_the_real_engine_start_gets_no_bonus_either()
    {
        // There is no preset start of measurement: the engine records the start-up time (the real install case).
        using var env = new TestEnv(start: At(10, 1, 12), restDayBonus: true);
        env.Store.Dispose();
        File.Delete(env.DbPath);
        env.Restart(); // a new, empty database
        Assert.Equal(At(10, 1, 12), env.Engine.TrackingStart);
        Assert.Equal(3, env.Engine.GetLolStatus(A).Limit);
    }

    [Fact]
    public void Installed_in_the_middle_of_the_previous_day_is_still_not_a_full_day()
    {
        // Installed on Wednesday at 15:00; on Thursday the Wednesday day is only partly measured → no bonus.
        using var env = new TestEnv(start: At(10, 1, 9), restDayBonus: true, trackingStart: At(9, 30, 15));
        Assert.Equal(3, env.Engine.GetLolStatus(A).Limit);
        Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
    }

    [Fact]
    public void The_bonus_appears_once_the_previous_day_was_fully_tracked()
    {
        // Installed before Wednesday 00:00 → on Thursday the bonus is due for the full, empty Wednesday.
        using var env = new TestEnv(start: At(10, 1, 9), restDayBonus: true, trackingStart: At(9, 29, 23));
        Assert.Equal(4, env.Engine.GetLolStatus(A).Limit);
        Assert.Equal(TimeSpan.FromMinutes(90), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
    }

    [Fact]
    public void Install_day_flow_second_full_day_earns_the_bonus_but_the_first_day_does_not()
    {
        // Installed on Tuesday at 20:00. Wednesday: no bonus (Tuesday is not complete). Thursday: yes (Wednesday is complete and empty).
        using var env = new TestEnv(start: At(9, 29, 20), restDayBonus: true, trackingStart: At(9, 29, 20));
        Assert.Equal(3, env.Engine.GetLolStatus(A).Limit);

        env.Clock.UtcNow = At(9, 30, 10);
        Assert.Equal(3, env.Engine.GetLolStatus(A).Limit);

        env.Clock.UtcNow = At(10, 1, 10);
        Assert.Equal(4, env.Engine.GetLolStatus(A).Limit);
    }

    [Fact]
    public void Tracking_start_survives_restart_and_is_not_moved_forward()
    {
        using var env = new TestEnv(start: At(10, 1, 12), restDayBonus: true, trackingStart: At(9, 25, 8));
        env.Clock.UtcNow = At(10, 3, 12);
        env.Restart();
        Assert.Equal(At(9, 25, 8), env.Engine.TrackingStart);
    }

    [Fact]
    public void An_existing_database_without_a_tracking_value_uses_its_earliest_usage()
    {
        // Update from an older version: no stored start, but there is earlier usage → that is the start.
        using var env = new TestEnv(start: At(10, 1, 12));
        env.Store.AddWebUsage(A, WebCategory.ShortVideo, At(9, 20, 18), 60_000);
        using var fresh = new UsageStore(Path.Combine(Path.GetTempPath(), "limiter-tests-upgrade-" + Guid.NewGuid().ToString("N") + ".db"));
        fresh.AddWebUsage(A, WebCategory.ShortVideo, At(9, 20, 18), 60_000);
        fresh.TryRecordMatch(A, 1, At(9, 22, 20), 420);
        Assert.Equal(At(9, 20, 18), fresh.EnsureTrackingStart(At(10, 1, 12)));
        // A second call gives the same, even if time has passed meanwhile.
        Assert.Equal(At(9, 20, 18), fresh.EnsureTrackingStart(At(10, 5, 12)));
    }
}
