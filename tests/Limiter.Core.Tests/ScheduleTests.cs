using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

/// <summary>Napi visszaállítás és hétvégi mentesség. A naptár: 2026. október 1. csütörtök, 3. szombat, 5. hétfő.</summary>
public class ScheduleTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static DateTimeOffset Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    private static LimitSettings Daily(int hour, bool weekends = false) =>
        LimitSettings.Default with { ResetMode = ResetMode.Daily, DailyResetHour = hour, EnforceOnWeekends = weekends };

    private static void Watch(TestEnv env, WebCategory category, TimeSpan duration)
    {
        env.Engine.ReportWebUsage(TestEnv.UserA, category, Second);
        for (var t = Second; t < duration; t += Second)
        {
            env.Clock.Advance(Second);
            env.Engine.ReportWebUsage(TestEnv.UserA, category, Second);
        }
    }

    [Fact]
    public void Daily_reset_frees_the_whole_web_budget_at_the_chosen_hour()
    {
        using var env = new TestEnv(Daily(3), start: Utc(1, 20));
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(61));

        var status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.True(status.Blocked);
        Assert.Equal(Utc(2, 3), status.AvailableAgainAt);
        Assert.Equal(Utc(2, 3), status.NextReleaseAt);

        env.Clock.UtcNow = Utc(2, 2, 59);
        Assert.True(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Blocked);

        // 03:00-kor minden egyszerre felszabadul (nem fokozatosan, mint gördülő módban).
        env.Clock.UtcNow = Utc(2, 3);
        status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.False(status.Blocked);
        Assert.Equal(TimeSpan.FromMinutes(60), status.Remaining);
    }

    [Fact]
    public void Daily_reset_only_counts_usage_since_the_last_reset()
    {
        using var env = new TestEnv(Daily(3), start: Utc(2, 2, 30));
        Watch(env, WebCategory.FacebookFeed, TimeSpan.FromMinutes(20)); // 02:30–02:50: az előző napé

        env.Clock.UtcNow = Utc(2, 3, 10);
        Watch(env, WebCategory.FacebookFeed, TimeSpan.FromMinutes(5)); // 03:10–03:15: a mai napé

        Assert.InRange(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.FacebookFeed).Used.TotalMinutes, 4.9, 5.1);
    }

    [Fact]
    public void Daily_reset_frees_lol_matches_at_the_chosen_hour()
    {
        using var env = new TestEnv(Daily(3), start: Utc(1, 18));
        for (var i = 1; i <= 3; i++)
        {
            env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(i));
            env.Clock.Advance(TimeSpan.FromHours(1));
        }

        var status = env.Engine.GetLolStatus(TestEnv.UserA);
        Assert.True(status.Blocked);
        Assert.Equal(Utc(2, 3), status.AvailableAgainAt);
        Assert.Equal(MatchmakingAction.CancelSearch, env.Tracker.DecideMatchmaking(TestEnv.UserA, "Matchmaking", TestEnv.RankedSolo).Action);

        env.Clock.UtcNow = Utc(2, 3);
        status = env.Engine.GetLolStatus(TestEnv.UserA);
        Assert.False(status.Blocked);
        Assert.Equal(3, status.Remaining);
    }

    [Fact]
    public void Midnight_reset_hour_zero_works()
    {
        using var env = new TestEnv(Daily(0), start: Utc(1, 22));
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(61));
        Assert.True(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Blocked);
        Assert.Equal(Utc(2, 0), env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).AvailableAgainAt);
    }

    [Fact]
    public void Weekends_are_free_by_default_and_weekend_usage_is_not_recorded()
    {
        using var env = new TestEnv(start: Utc(3, 10)); // szombat
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(90));
        var status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.False(status.Blocked);
        Assert.True(status.Exempt);
        Assert.Equal(TimeSpan.Zero, status.Used);

        for (var i = 1; i <= 5; i++)
            Assert.Equal(MatchRecordOutcome.Exempt, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(i)));
        Assert.Equal(MatchmakingAction.Allow, env.Tracker.DecideMatchmaking(TestEnv.UserA, "Matchmaking", TestEnv.RankedSolo).Action);

        // Hétfőn tiszta lappal indul.
        env.Clock.UtcNow = Utc(5, 9);
        Assert.Equal(0, env.Engine.GetLolStatus(TestEnv.UserA).Used);
        Assert.Equal(TimeSpan.Zero, env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Used);
    }

    [Fact]
    public void A_game_started_on_sunday_night_does_not_count_when_it_runs_into_monday()
    {
        using var env = new TestEnv(start: Utc(4, 23, 50)); // vasárnap
        Assert.Equal(MatchRecordOutcome.Exempt, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(42)));
        env.Clock.UtcNow = Utc(5, 0, 20); // hétfő
        Assert.Equal(MatchRecordOutcome.AlreadyRecorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(42)));
        Assert.Equal(0, env.Engine.GetLolStatus(TestEnv.UserA).Used);
        Assert.Empty(env.Tracker.PendingRemakeChecks(TestEnv.UserA));
    }

    [Fact]
    public void Weekend_follows_the_daily_reset_hour()
    {
        using var env = new TestEnv(Daily(3), start: Utc(3, 2)); // szombat 02:00 = még a péntek 03:00-kor kezdődött nap
        Assert.False(env.Engine.IsExemptNow);
        env.Clock.UtcNow = Utc(3, 3);
        Assert.True(env.Engine.IsExemptNow);
        env.Clock.UtcNow = Utc(5, 2, 59); // hétfő hajnal = még a vasárnapi nap
        Assert.True(env.Engine.IsExemptNow);
        env.Clock.UtcNow = Utc(5, 3);
        Assert.False(env.Engine.IsExemptNow);
    }

    [Fact]
    public void Weekend_enforcement_can_be_turned_on()
    {
        using var env = new TestEnv(LimitSettings.Default with { EnforceOnWeekends = true }, start: Utc(3, 10));
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(61));
        var status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.True(status.Blocked);
        Assert.False(status.Exempt);
        Assert.Equal(MatchRecordOutcome.Recorded, env.Tracker.OnGameflow(TestEnv.UserA, TestEnv.Running(1)));
    }

    [Fact]
    public void Block_on_friday_evening_lifts_when_the_free_weekend_starts()
    {
        using var env = new TestEnv(start: Utc(2, 20)); // péntek, gördülő mód
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(61));
        var status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.True(status.Blocked);
        Assert.Equal(Utc(3, 0), status.AvailableAgainAt); // nem szombat 20:00, hanem a hétvége kezdete

        env.Clock.UtcNow = Utc(3, 0);
        Assert.False(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Blocked);
    }

    [Fact]
    public void Daily_reset_in_local_time_handles_daylight_saving_change()
    {
        var budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
        // 2027. március 28. (vasárnap) 02:00 helyi idő nem létezik (02:00 → 03:00).
        var settings = Daily(2, weekends: true);
        using var env = new TestEnv(settings, budapest, start: new DateTimeOffset(2027, 3, 27, 12, 0, 0, TimeSpan.FromHours(1)));
        var reset = env.Engine.Schedule.NextDayStart(env.Clock.UtcNow);
        Assert.Equal(new DateTimeOffset(2027, 3, 28, 3, 0, 0, TimeSpan.FromHours(2)), reset);

        // Nyári időben a 02:00 helyi idő = 00:00 UTC.
        env.Clock.UtcNow = new DateTimeOffset(2027, 6, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2027, 6, 2, 0, 0, 0, TimeSpan.Zero), env.Engine.Schedule.NextDayStart(env.Clock.UtcNow));
    }

    [Fact]
    public void Settings_are_sanitized()
    {
        var s = new LimitSettings { ResetMode = (ResetMode)7, DailyResetHour = 30, ShortVideoMinutes = -5, FacebookFeedMinutes = 5000 }.Sanitized();
        Assert.Equal(ResetMode.Rolling, s.ResetMode);
        Assert.Equal(23, s.DailyResetHour);
        Assert.Equal(0, s.ShortVideoMinutes);
        Assert.Equal(24 * 60, s.FacebookFeedMinutes);
        Assert.False(LimitSettings.Default.EnforceOnWeekends);
        Assert.Equal(ResetMode.Rolling, LimitSettings.Default.ResetMode);
    }

    [Fact]
    public void Parses_the_limits_json_written_by_the_installer()
    {
        // Pontosan az install.ps1 által írt formátum (ConvertTo-Json).
        var s = LimitSettings.Parse("""
            {
                "resetMode":  "daily",
                "dailyResetHour":  3,
                "lolPvpMatches":  3,
                "shortVideoMinutes":  45,
                "facebookFeedMinutes":  30,
                "enforceOnWeekends":  true
            }
            """);
        Assert.Equal(ResetMode.Daily, s.ResetMode);
        Assert.Equal(3, s.DailyResetHour);
        Assert.Equal(45, s.ShortVideoMinutes);
        Assert.Equal(30, s.FacebookFeedMinutes);
        Assert.True(s.EnforceOnWeekends);

        // Régi (1.0-s) fájl új mezők nélkül: alapértelmezések.
        var old = LimitSettings.Parse("""{ "lolPvpMatches": 3, "shortVideoMinutes": 60, "facebookFeedMinutes": 60 }""");
        Assert.Equal(ResetMode.Rolling, old.ResetMode);
        Assert.False(old.EnforceOnWeekends);

        Assert.Equal(s, LimitSettings.Parse(s.ToJson()));
    }
}
