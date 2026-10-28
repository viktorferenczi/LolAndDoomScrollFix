using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

/// <summary>Banned hours. Calendar: Thursday 1 October 2026, Saturday the 3rd; UTC time zone.</summary>
public class BannedHoursTests
{
    private const string A = TestEnv.UserA;

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    private static BannedHours Ban(int from, int to) => new() { Enabled = true, From = from, To = to };

    private static LimitSettings AllBanned(int from, int to, bool weekends = false) => LimitSettings.Default with
    {
        LolBannedHours = Ban(from, to),
        ShortVideoBannedHours = Ban(from, to),
        FacebookFeedBannedHours = Ban(from, to),
        EnforceOnWeekends = weekends,
    };

    [Theory]
    [InlineData(1, 5, 0, false)]
    [InlineData(1, 5, 1, true)]
    [InlineData(1, 5, 4, true)]
    [InlineData(1, 5, 5, false)]
    [InlineData(22, 6, 21, false)]
    [InlineData(22, 6, 22, true)]
    [InlineData(22, 6, 0, true)]
    [InlineData(22, 6, 5, true)]
    [InlineData(22, 6, 6, false)]
    public void Window_covers_from_inclusive_to_exclusive_and_can_wrap_midnight(int from, int to, int hour, bool expected) =>
        Assert.Equal(expected, Ban(from, to).Covers(hour));

    [Fact]
    public void Same_start_and_end_or_disabled_means_no_ban()
    {
        Assert.False(Ban(3, 3).Sanitized().Enabled);
        Assert.False(BannedHours.Disabled.Covers(3));
        Assert.Equal(23, new BannedHours { Enabled = true, From = 40, To = 2 }.Sanitized().From);
    }

    [Fact]
    public void Lol_search_is_cancelled_during_the_ban_even_with_matches_left()
    {
        using var env = new TestEnv(AllBanned(1, 5), start: At(1, 2));
        var status = env.Engine.GetLolStatus(A);
        Assert.True(status.Blocked);
        Assert.True(status.Banned);
        Assert.Equal(3, status.Remaining); // the limit is not used up
        Assert.Equal(At(1, 5), status.AvailableAgainAt);

        var decision = env.Tracker.DecideMatchmaking(A, "Matchmaking", TestEnv.RankedSolo);
        Assert.Equal(MatchmakingAction.CancelSearch, decision.Action);
        Assert.True(decision.Banned);
        Assert.Equal(MatchmakingAction.DeclineReadyCheck, env.Tracker.DecideMatchmaking(A, "ReadyCheck", TestEnv.RankedSolo).Action);

        // TFT, practice etc. are allowed in a banned period too.
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(A, "Matchmaking", TestEnv.TftNormal).Action);

        env.Clock.UtcNow = At(1, 5);
        Assert.False(env.Engine.GetLolStatus(A).Blocked);
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(A, "Matchmaking", TestEnv.RankedSolo).Action);
    }

    [Fact]
    public void A_game_running_into_the_ban_can_finish_and_reconnect()
    {
        using var env = new TestEnv(AllBanned(1, 5), start: At(1, 0, 40));
        Assert.Equal(MatchRecordOutcome.Recorded, env.Tracker.OnGameflow(A, TestEnv.Running(7)));
        env.Clock.UtcNow = At(1, 1, 10);
        Assert.Equal(MatchRecordOutcome.AlreadyRecorded, env.Tracker.OnGameflow(A, TestEnv.Running(7, phase: "Reconnect")));
        Assert.Equal(1, env.Engine.GetLolStatus(A).Used);
    }

    [Fact]
    public void Web_categories_are_blocked_during_their_own_ban_only()
    {
        var settings = LimitSettings.Default with { ShortVideoBannedHours = Ban(22, 6) };
        using var env = new TestEnv(settings, start: At(1, 23));

        var shortVideo = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.True(shortVideo.Blocked);
        Assert.True(shortVideo.Banned);
        Assert.Equal(TimeSpan.FromMinutes(60), shortVideo.Remaining);
        Assert.Equal(At(2, 6), shortVideo.AvailableAgainAt); // across midnight

        Assert.False(env.Engine.GetWebStatus(A, WebCategory.FacebookFeed).Blocked);
        Assert.False(env.Engine.GetLolStatus(A).Blocked);
    }

    [Fact]
    public void Ban_does_not_change_the_budget_or_the_reset()
    {
        var settings = AllBanned(1, 5) with { ResetMode = ResetMode.Daily, DailyResetHour = 3 };
        using var env = new TestEnv(settings, start: At(1, 20));
        for (var i = 0; i < 10; i++) env.Store.AddWebUsage(A, WebCategory.ShortVideo, At(1, 20, i), 60_000);

        env.Clock.UtcNow = At(2, 2);
        var before = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.True(before.Banned);
        Assert.Equal(TimeSpan.FromMinutes(10), before.Used);

        // At 03:00 the reset also happens during the ban; at 05:00 it opens with a clean limit.
        env.Clock.UtcNow = At(2, 3, 30);
        Assert.Equal(TimeSpan.Zero, env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Used);
        env.Clock.UtcNow = At(2, 5);
        var after = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.False(after.Blocked);
        Assert.Equal(TimeSpan.FromMinutes(60), after.Remaining);
    }

    [Fact]
    public void When_the_budget_is_also_used_up_the_later_release_wins()
    {
        // Rolling mode: the limit is used up between 00:30 and 01:31 and released 24 hours later; the ban (01-05) ends sooner than that.
        using var env = new TestEnv(AllBanned(1, 5), start: At(1, 2));
        for (var i = 0; i < 61; i++) env.Store.AddWebUsage(A, WebCategory.ShortVideo, At(1, 0, 30) + TimeSpan.FromMinutes(i), 60_000);

        var status = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.True(status.Blocked);
        Assert.True(status.Banned);
        Assert.True(status.AvailableAgainAt > At(1, 5));
        Assert.Equal(At(2, 0, 32), status.AvailableAgainAt);
    }

    [Fact]
    public void Free_weekends_have_no_ban_unless_weekend_limits_are_on()
    {
        using (var env = new TestEnv(AllBanned(1, 5), start: At(3, 2)))
            Assert.False(env.Engine.GetLolStatus(A).Blocked);

        using (var env = new TestEnv(AllBanned(1, 5, weekends: true), start: At(3, 2)))
            Assert.True(env.Engine.GetLolStatus(A).Banned);
    }

    [Fact]
    public void Ban_follows_local_time()
    {
        var budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
        // 1 October 2026 23:30 UTC = 01:30 local time (summer time, UTC+2).
        using var env = new TestEnv(AllBanned(1, 5), budapest, start: At(1, 23, 30));
        var status = env.Engine.GetLolStatus(A);
        Assert.True(status.Banned);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 5, 0, 0, TimeSpan.FromHours(2)), status.AvailableAgainAt);
    }

    [Fact]
    public void Parses_banned_hours_from_limits_json()
    {
        var s = LimitSettings.Parse("""
            {
              "lolBannedHours": { "enabled": true, "from": 1, "to": 5 },
              "shortVideoBannedHours": { "enabled": true, "from": 22, "to": 6 },
              "facebookFeedBannedHours": { "enabled": true, "from": 4, "to": 4 }
            }
            """);
        Assert.Equal(Ban(1, 5), s.LolBannedHours);
        Assert.Equal(Ban(22, 6), s.ShortVideoBannedHours);
        Assert.False(s.FacebookFeedBannedHours.Enabled);

        // An old file without the fields: no ban.
        Assert.False(LimitSettings.Parse("""{ "shortVideoMinutes": 60 }""").LolBannedHours.Enabled);
        Assert.Equal(s, LimitSettings.Parse(s.ToJson()));
    }
}
