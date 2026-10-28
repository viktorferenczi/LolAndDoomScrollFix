using System.Text.Json;
using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

/// <summary>
/// Az admin által egy napra adott extra keret. Naptár: 2026. október 1. csütörtök, 2. péntek, 3. szombat,
/// 4. vasárnap, 5. hétfő.
/// </summary>
public class ExtraGrantTests
{
    private const string A = TestEnv.UserA;
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    private static DayGrant Grant(int day, int lol = 0, int video = 0, int feed = 0) =>
        new() { Date = new DateOnly(2026, 10, day), LolMatches = lol, ShortVideoMinutes = video, FacebookFeedMinutes = feed };

    private static LimitSettings With(params DayGrant[] grants) => LimitSettings.Default with { ExtraGrants = grants };

    // ---------- hatás a keretre ----------

    [Fact]
    public void A_grant_for_today_adds_to_the_limits()
    {
        using var env = new TestEnv(With(Grant(1, lol: 2, video: 15, feed: 10)));
        var lol = env.Engine.GetLolStatus(A);
        Assert.Equal(5, lol.Limit);
        Assert.Equal(2, lol.Extra);
        var video = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.Equal(TimeSpan.FromMinutes(75), video.Limit);
        Assert.Equal(TimeSpan.FromMinutes(15), video.Extra);
        Assert.Equal(TimeSpan.FromMinutes(70), env.Engine.GetWebStatus(A, WebCategory.FacebookFeed).Limit);
    }

    [Fact]
    public void A_grant_for_another_day_does_not_apply_today()
    {
        using var env = new TestEnv(With(Grant(2, lol: 5, video: 60, feed: 60)));
        Assert.Equal(3, env.Engine.GetLolStatus(A).Limit);
        Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
    }

    [Fact]
    public void The_extra_lets_the_user_play_more_matches()
    {
        using var env = new TestEnv(With(Grant(1, lol: 1)));
        for (var i = 1; i <= 3; i++) env.Store.TryRecordMatch(A, i, At(1, 8, i), 420);
        Assert.False(env.Engine.GetLolStatus(A).Blocked); // 3 / 4
        env.Store.TryRecordMatch(A, 4, At(1, 9), 420);
        Assert.True(env.Engine.GetLolStatus(A).Blocked);
    }

    [Fact]
    public void The_extra_lets_the_user_scroll_longer()
    {
        using var env = new TestEnv(With(Grant(1, video: 10)));
        for (var i = 0; i < 65; i++) env.Store.AddWebUsage(A, WebCategory.ShortVideo, At(1, 8) + i * Minute, 60_000);
        Assert.False(env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Blocked);
        for (var i = 65; i < 70; i++) env.Store.AddWebUsage(A, WebCategory.ShortVideo, At(1, 8) + i * Minute, 60_000);
        Assert.True(env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Blocked);
    }

    [Fact]
    public void A_zero_base_limit_plus_a_grant_allows_exactly_the_grant()
    {
        var s = With(Grant(1, lol: 1, video: 5)) with { LolPvpMatches = 0, ShortVideoMinutes = 0 };
        using var env = new TestEnv(s);
        Assert.Equal(1, env.Engine.GetLolStatus(A).Limit);
        Assert.False(env.Engine.GetLolStatus(A).Blocked);
        Assert.Equal(TimeSpan.FromMinutes(5), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
    }

    [Fact]
    public void The_grant_stacks_with_the_rest_day_bonus()
    {
        using var env = new TestEnv(With(Grant(1, lol: 2, video: 10)), start: At(1, 12), restDayBonus: true);
        // Szerdán semmi sem fogyott → +1 meccs, +30 perc jutalom, mellé az extra.
        Assert.Equal(6, env.Engine.GetLolStatus(A).Limit);
        Assert.Equal(TimeSpan.FromMinutes(100), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
    }

    [Fact]
    public void A_banned_hour_still_blocks_despite_the_extra()
    {
        var s = With(Grant(1, video: 60)) with { ShortVideoBannedHours = new BannedHours { Enabled = true, From = 10, To = 14 } };
        using var env = new TestEnv(s);
        var video = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.True(video.Banned);
        Assert.True(video.Blocked);
    }

    [Fact]
    public void A_grant_on_an_exempt_weekend_has_no_effect()
    {
        using var env = new TestEnv(With(Grant(3, lol: 5)), start: At(3, 12));
        var lol = env.Engine.GetLolStatus(A);
        Assert.True(lol.Exempt);
        Assert.Equal(0, lol.Extra);
        Assert.Equal(3, lol.Limit);
    }

    [Fact]
    public void A_grant_on_a_weekend_applies_when_weekends_are_enforced()
    {
        using var env = new TestEnv(With(Grant(3, lol: 2)) with { EnforceOnWeekends = true }, start: At(3, 12));
        Assert.Equal(5, env.Engine.GetLolStatus(A).Limit);
    }

    // ---------- gördülő mód: a nap éjféltől éjfélig tart ----------

    [Fact]
    public void Rolling_the_extra_ends_at_midnight_and_the_release_time_accounts_for_it()
    {
        var s = With(Grant(1, lol: 1));
        using var env = new TestEnv(s, start: At(1, 22));
        for (var i = 1; i <= 4; i++) env.Store.TryRecordMatch(A, i, At(1, 10, i), 420);
        Assert.Equal(4, env.Engine.GetLolStatus(A).Limit);
        Assert.True(env.Engine.GetLolStatus(A).Blocked);

        // Éjfél után az extra megszűnik: 4 meccs az ablakban 3-as kerettel is tilt. Kettőnek kell kiesnie (10:01, 10:02),
        // hogy 3 alá kerüljön a számláló, vagyis 10:02-kor oldódik fel.
        env.Clock.UtcNow = At(2, 1);
        var lol = env.Engine.GetLolStatus(A);
        Assert.Equal(3, lol.Limit);
        Assert.True(lol.Blocked);
        Assert.Equal(At(2, 10, 2), lol.AvailableAgainAt);
    }

    [Fact]
    public void Rolling_a_grant_for_tomorrow_shortens_the_wait_when_blocked_today()
    {
        var s = With(Grant(2, video: 30));
        using var env = new TestEnv(s, start: At(1, 22));
        for (var i = 0; i < 60; i++) env.Store.AddWebUsage(A, WebCategory.ShortVideo, At(1, 20) + i * Minute, 60_000);
        var video = env.Engine.GetWebStatus(A, WebCategory.ShortVideo);
        Assert.True(video.Blocked);
        // Holnap 0:00-tól a keret 90 perc, a 60 percnyi használat alatta van → ott oldódik fel.
        Assert.Equal(At(2, 0), video.AvailableAgainAt);
    }

    // ---------- napi mód: a nap a visszaállítás órájától tart ----------

    [Fact]
    public void Daily_before_the_reset_hour_the_current_day_is_still_yesterdays_date()
    {
        var s = With(Grant(1, lol: 2)) with { ResetMode = ResetMode.Daily, DailyResetHour = 3 };
        using var env = new TestEnv(s, start: At(2, 1)); // pénteken 01:00 → még a csütörtökön kezdődött nap
        Assert.Equal(5, env.Engine.GetLolStatus(A).Limit);
        env.Clock.UtcNow = At(2, 3); // 03:00 → péntek, extra nélkül
        Assert.Equal(3, env.Engine.GetLolStatus(A).Limit);
    }

    [Fact]
    public void Daily_the_extra_applies_from_the_reset_hour()
    {
        var s = With(Grant(2, video: 30)) with { ResetMode = ResetMode.Daily, DailyResetHour = 3 };
        using var env = new TestEnv(s, start: At(2, 2, 59));
        Assert.Equal(TimeSpan.FromMinutes(60), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
        env.Clock.UtcNow = At(2, 3);
        Assert.Equal(TimeSpan.FromMinutes(90), env.Engine.GetWebStatus(A, WebCategory.ShortVideo).Limit);
    }

    [Fact]
    public void Daily_weekend_exemption_follows_the_day_start_date()
    {
        // Szombaton 02:00 még a péntekről induló nap (hétköznap) tart; az extra rá érvényes.
        var s = With(Grant(2, lol: 1)) with { ResetMode = ResetMode.Daily, DailyResetHour = 3 };
        using var env = new TestEnv(s, start: At(3, 2));
        Assert.False(env.Engine.GetLolStatus(A).Exempt);
        Assert.Equal(4, env.Engine.GetLolStatus(A).Limit);
    }

    // ---------- napválasztó ----------

    [Fact]
    public void Day_options_start_today_and_cover_the_horizon()
    {
        var options = GrantDays.Options(LimitSettings.Default, At(1, 12), TimeZoneInfo.Utc);
        Assert.Equal(GrantDays.HorizonDays + 1, options.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), options[0].Date);
        Assert.True(options[0].IsToday);
        Assert.All(options.Skip(1), o => Assert.False(o.IsToday));
        Assert.Equal(new DateOnly(2026, 10, 31), options[^1].Date);
    }

    [Fact]
    public void Weekends_are_not_selectable_unless_enforced()
    {
        var options = GrantDays.Options(LimitSettings.Default, At(1, 12), TimeZoneInfo.Utc);
        Assert.Equal(GrantDayStatus.ExemptWeekend, options.Single(o => o.Date == new DateOnly(2026, 10, 3)).Status);
        Assert.Equal(GrantDayStatus.ExemptWeekend, options.Single(o => o.Date == new DateOnly(2026, 10, 4)).Status);
        Assert.True(options.Single(o => o.Date == new DateOnly(2026, 10, 5)).Selectable);

        var enforced = GrantDays.Options(LimitSettings.Default with { EnforceOnWeekends = true }, At(1, 12), TimeZoneInfo.Utc);
        Assert.All(enforced, o => Assert.True(o.Selectable));
    }

    [Fact]
    public void Day_options_in_daily_mode_before_the_reset_hour_start_with_yesterdays_date()
    {
        var s = LimitSettings.Default with { ResetMode = ResetMode.Daily, DailyResetHour = 3 };
        var options = GrantDays.Options(s, At(2, 1), TimeZoneInfo.Utc);
        Assert.Equal(new DateOnly(2026, 10, 1), options[0].Date);
        Assert.True(options[0].IsToday);
    }

    [Fact]
    public void Saturday_morning_before_reset_still_offers_the_weekday_that_is_running()
    {
        var s = LimitSettings.Default with { ResetMode = ResetMode.Daily, DailyResetHour = 3 };
        var options = GrantDays.Options(s, At(3, 1), TimeZoneInfo.Utc);
        Assert.Equal(new DateOnly(2026, 10, 2), options[0].Date); // péntek
        Assert.True(options[0].Selectable);
        Assert.Equal(GrantDayStatus.ExemptWeekend, options[1].Status); // szombat
    }

    [Fact]
    public void A_weekend_start_in_the_weekend_leaves_the_first_option_disabled()
    {
        var options = GrantDays.Options(LimitSettings.Default, At(3, 12), TimeZoneInfo.Utc);
        Assert.False(options[0].Selectable);
        Assert.True(options[0].IsToday);
    }

    [Fact]
    public void Existing_grants_are_flagged()
    {
        var options = GrantDays.Options(With(Grant(5, lol: 1)), At(1, 12), TimeZoneInfo.Utc);
        Assert.True(options.Single(o => o.Date == new DateOnly(2026, 10, 5)).HasGrant);
        Assert.False(options.Single(o => o.Date == new DateOnly(2026, 10, 6)).HasGrant);
    }

    [Fact]
    public void Day_options_use_the_local_time_zone()
    {
        // 2026-10-01 23:30 UTC már péntek 01:30 Budapesten.
        var budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
        var options = GrantDays.Options(LimitSettings.Default, At(1, 23, 30), budapest);
        Assert.Equal(new DateOnly(2026, 10, 2), options[0].Date);
    }

    [Fact]
    public void Day_options_across_the_dst_change_are_plain_calendar_days()
    {
        // Az őszi óraátállítás 2026. október 25-én van Budapesten; a napok sorozata nem sérül.
        var budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
        var options = GrantDays.Options(LimitSettings.Default with { EnforceOnWeekends = true },
            new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero), budapest);
        var dates = options.Select(o => o.Date).ToList();
        Assert.Contains(new DateOnly(2026, 10, 25), dates);
        Assert.Contains(new DateOnly(2026, 10, 26), dates);
        Assert.Equal(dates.Count, dates.Distinct().Count());
    }

    [Fact]
    public void Pruning_removes_only_expired_grants()
    {
        var s = With(Grant(1, lol: 1), Grant(2, lol: 1), Grant(5, lol: 1));
        var pruned = GrantDays.Pruned(s, At(2, 12), TimeZoneInfo.Utc);
        Assert.Equal([new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5)], pruned.ExtraGrants.Select(g => g.Date).ToArray());
    }

    // ---------- beállítások: ellenőrzés, tárolás ----------

    [Fact]
    public void Validate_flags_empty_duplicate_out_of_range_and_weekend_grants()
    {
        Assert.Empty(SettingsFile.Validate(With(Grant(5, lol: 1)), TimeZoneInfo.Utc));

        Assert.Contains(SettingsFile.Validate(With(Grant(5)), TimeZoneInfo.Utc), e => e.Contains("enter at least one value"));
        Assert.Contains(SettingsFile.Validate(With(Grant(5, lol: 1), Grant(5, video: 5)), TimeZoneInfo.Utc), e => e.Contains("more than one entry"));
        Assert.Contains(SettingsFile.Validate(With(Grant(5, lol: 101)), TimeZoneInfo.Utc), e => e.Contains("between 0 and 100"));
        Assert.Contains(SettingsFile.Validate(With(Grant(5, video: 1441)), TimeZoneInfo.Utc), e => e.Contains("between 0 and 1440"));
        Assert.Contains(SettingsFile.Validate(With(Grant(3, lol: 1)), TimeZoneInfo.Utc), e => e.Contains("no limit on weekends"));
        Assert.Empty(SettingsFile.Validate(With(Grant(3, lol: 1)) with { EnforceOnWeekends = true }, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Grants_survive_a_json_round_trip_with_the_date_as_text()
    {
        var s = With(Grant(5, lol: 2, video: 15, feed: 0));
        var json = s.ToJson();
        Assert.Contains("\"2026-10-05\"", json);
        Assert.DoesNotContain("isEmpty", json); // számított tulajdonság, nem kerül a fájlba
        var back = LimitSettings.Parse(json);
        Assert.Equal(s.ExtraGrants, back.ExtraGrants);
    }

    [Fact]
    public void Old_limits_files_without_grants_load_with_an_empty_list()
    {
        var back = LimitSettings.Parse("""{"lolPvpMatches": 4}""");
        Assert.Empty(back.ExtraGrants);
        Assert.Equal(4, back.LolPvpMatches);
    }

    [Fact]
    public void Sanitizing_drops_empty_entries_merges_duplicates_sorts_and_clamps()
    {
        var s = LimitSettings.Default with
        {
            ExtraGrants = [Grant(9, lol: 1), Grant(5), Grant(7, lol: 1000, video: -5, feed: 99999), Grant(9, video: 20)],
        };
        var clean = s.Sanitized().ExtraGrants;
        Assert.Equal([new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 9)], clean.Select(g => g.Date).ToArray());
        Assert.Equal(DayGrant.MaxMatches, clean[0].LolMatches);
        Assert.Equal(0, clean[0].ShortVideoMinutes);
        Assert.Equal(DayGrant.MaxMinutes, clean[0].FacebookFeedMinutes);
        Assert.Equal(20, clean[1].ShortVideoMinutes); // az utolsó bejegyzés nyer
    }

    [Fact]
    public void A_json_entry_without_a_date_is_ignored()
    {
        var back = LimitSettings.Parse("""{"extraGrants":[{"lolMatches":3}]}""");
        Assert.Empty(back.ExtraGrants);
    }

    [Fact]
    public void Garbage_dates_make_the_file_unreadable_rather_than_silently_wrong()
    {
        Assert.Throws<JsonException>(() => LimitSettings.Parse("""{"extraGrants":[{"date":"nem-datum","lolMatches":3}]}"""));
    }

    [Fact]
    public void WithGrant_replaces_and_an_empty_grant_removes()
    {
        var s = With(Grant(5, lol: 1)).WithGrant(Grant(5, video: 10));
        Assert.Single(s.ExtraGrants);
        Assert.Equal(0, s.ExtraGrants[0].LolMatches);
        Assert.Empty(s.WithGrant(Grant(5)).ExtraGrants);
        Assert.Empty(s.WithoutGrant(new DateOnly(2026, 10, 5)).ExtraGrants);
    }
}
