namespace Limiter.Core.Tests;

public class WebLimitTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    /// <summary>Simulates continuous watching: one report per second, as the extension sends it.</summary>
    private static void Watch(TestEnv env, WebCategory category, TimeSpan duration, string sid = TestEnv.UserA)
    {
        for (var t = TimeSpan.Zero; t < duration; t += Second)
        {
            env.Clock.Advance(Second);
            env.Engine.ReportWebUsage(sid, category, Second);
        }
    }

    [Fact]
    public void Sixty_minutes_block_and_time_is_released_gradually_after_24_hours()
    {
        using var env = new TestEnv();
        var start = env.Clock.UtcNow;
        env.Engine.ReportWebUsage(TestEnv.UserA, WebCategory.ShortVideo, Second); // first report: no history
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(60));

        var status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.True(status.Blocked);
        Assert.Equal(TimeSpan.Zero, status.Remaining);
        Assert.NotNull(status.AvailableAgainAt);

        // After 23 hours 59 minutes it is still blocked.
        env.Clock.UtcNow = start + TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59);
        Assert.True(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Blocked);

        // 24 hours + 2 minutes after the start of watching, about 1-2 minutes have been released — gradually, not at once.
        env.Clock.UtcNow = start + LimitEngine.Window + TimeSpan.FromMinutes(2);
        status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.False(status.Blocked);
        Assert.InRange(status.Remaining.TotalMinutes, 0.5, 2.5);

        // 30 minutes later about 30 minutes are free.
        env.Clock.UtcNow = start + LimitEngine.Window + TimeSpan.FromMinutes(31);
        Assert.InRange(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Remaining.TotalMinutes, 29, 32);

        // The whole limit is released after 24 hours + 61 minutes.
        env.Clock.UtcNow = start + LimitEngine.Window + TimeSpan.FromMinutes(62);
        status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.Equal(TimeSpan.FromMinutes(60), status.Remaining);
        Assert.Null(status.NextReleaseAt);
    }

    [Fact]
    public void Available_again_time_matches_the_moment_the_block_lifts()
    {
        using var env = new TestEnv();
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(61));
        var status = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo);
        Assert.True(status.Blocked);

        env.Clock.UtcNow = status.AvailableAgainAt!.Value - Second;
        Assert.True(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Blocked);
        env.Clock.UtcNow = status.AvailableAgainAt!.Value + Second;
        Assert.False(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Blocked);
    }

    [Fact]
    public void Short_video_and_facebook_feed_have_separate_budgets()
    {
        using var env = new TestEnv();
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(60));
        Watch(env, WebCategory.FacebookFeed, TimeSpan.FromMinutes(10));

        Assert.True(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Blocked);
        var feed = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.FacebookFeed);
        Assert.False(feed.Blocked);
        Assert.InRange(feed.Used.TotalMinutes, 9.9, 10.1);
    }

    [Fact]
    public void Two_chrome_profiles_reporting_simultaneously_share_one_budget_without_double_counting()
    {
        using var env = new TestEnv();
        var half = TimeSpan.FromMilliseconds(500);
        env.Engine.ReportWebUsage(TestEnv.UserA, WebCategory.ShortVideo, Second);

        // Profiles A and B with a half-second offset, each reporting 1 second per second.
        for (var i = 0; i < 600; i++)
        {
            env.Clock.Advance(half);
            env.Engine.ReportWebUsage(TestEnv.UserA, WebCategory.ShortVideo, Second); // A profil
            env.Clock.Advance(half);
            env.Engine.ReportWebUsage(TestEnv.UserA, WebCategory.ShortVideo, Second); // B profil
        }

        var used = env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Used;
        Assert.InRange(used.TotalSeconds, 599, 602); // 10 minutes of real time, not 20
    }

    [Fact]
    public void Usage_survives_restart()
    {
        using var env = new TestEnv();
        Watch(env, WebCategory.FacebookFeed, TimeSpan.FromMinutes(5));
        env.Restart();
        Assert.InRange(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.FacebookFeed).Used.TotalMinutes, 4.9, 5.1);
    }

    [Fact]
    public void Oversized_and_negative_claims_are_clamped()
    {
        using var env = new TestEnv();
        Assert.Equal(TimeSpan.Zero, env.Engine.ReportWebUsage(TestEnv.UserA, WebCategory.ShortVideo, TimeSpan.FromSeconds(-30)));
        Assert.Equal(LimitEngine.MaxClaim, env.Engine.ReportWebUsage(TestEnv.UserA, WebCategory.ShortVideo, TimeSpan.FromHours(5)));
        // Right after, another big report: the elapsed time is 0, so nothing is accepted.
        Assert.Equal(TimeSpan.Zero, env.Engine.ReportWebUsage(TestEnv.UserA, WebCategory.ShortVideo, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Users_are_isolated()
    {
        using var env = new TestEnv();
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(60), TestEnv.UserA);
        Assert.Equal(TimeSpan.Zero, env.Engine.GetWebStatus(TestEnv.UserB, WebCategory.ShortVideo).Used);
    }

    [Fact]
    public void Prune_keeps_the_current_window()
    {
        using var env = new TestEnv();
        Watch(env, WebCategory.ShortVideo, TimeSpan.FromMinutes(3));
        env.Store.Prune(env.Clock.UtcNow - TimeSpan.FromDays(3));
        Assert.InRange(env.Engine.GetWebStatus(TestEnv.UserA, WebCategory.ShortVideo).Used.TotalMinutes, 2.9, 3.1);
    }
}
