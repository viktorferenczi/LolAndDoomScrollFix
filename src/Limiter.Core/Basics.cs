namespace Limiter.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>A webes időkeretek. Egy másodperc mindig legfeljebb az egyiket fogyasztja.</summary>
public enum WebCategory
{
    /// <summary>Instagram Reels, TikTok, YouTube Shorts és Facebook Reels — közös keret.</summary>
    ShortVideo = 1,

    /// <summary>A Facebook kezdőlapi hírfolyama — külön keret.</summary>
    FacebookFeed = 2,
}

public static class WebCategoryNames
{
    public const string ShortVideo = "shortVideo";
    public const string FacebookFeed = "facebookFeed";

    public static bool TryParse(string? value, out WebCategory category)
    {
        switch (value)
        {
            case ShortVideo: category = WebCategory.ShortVideo; return true;
            case FacebookFeed: category = WebCategory.FacebookFeed; return true;
            default: category = default; return false;
        }
    }
}

/// <summary>Az adminisztrátor által módosítható korlátok (limits.json).</summary>
public sealed record LimitSettings
{
    public int LolPvpMatches { get; init; } = 3;
    public int ShortVideoMinutes { get; init; } = 60;
    public int FacebookFeedMinutes { get; init; } = 60;

    public static LimitSettings Default { get; } = new();

    public TimeSpan WebLimit(WebCategory category) => TimeSpan.FromMinutes(category switch
    {
        WebCategory.ShortVideo => ShortVideoMinutes,
        WebCategory.FacebookFeed => FacebookFeedMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    });

    public LimitSettings Sanitized() => this with
    {
        LolPvpMatches = Math.Clamp(LolPvpMatches, 0, 1000),
        ShortVideoMinutes = Math.Clamp(ShortVideoMinutes, 0, 24 * 60),
        FacebookFeedMinutes = Math.Clamp(FacebookFeedMinutes, 0, 24 * 60),
    };
}
