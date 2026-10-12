using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>Mikor szabadul fel a felhasznált keret.</summary>
public enum ResetMode
{
    /// <summary>Gördülő 24 óra: minden felhasználás pontosan 24 órával később szabadul fel.</summary>
    Rolling = 0,

    /// <summary>Naponta egyszer, a megadott órában minden keret egyszerre nullázódik.</summary>
    Daily = 1,
}

/// <summary>
/// Tiltott időszak egy napon belül (helyi idő, egész órák): <see cref="From"/>-tól (benne) <see cref="To"/>-ig (nincs benne).
/// Átnyúlhat éjfélen (pl. 22 → 6). A keretet és a nullázást nem érinti, csak közben nem lehet használni.
/// </summary>
public sealed record BannedHours
{
    public bool Enabled { get; init; }
    public int From { get; init; } = 1;
    public int To { get; init; } = 5;

    public static BannedHours Disabled { get; } = new();

    public BannedHours Sanitized()
    {
        var from = Math.Clamp(From, 0, 23);
        var to = Math.Clamp(To, 0, 23);
        return this with { From = from, To = to, Enabled = Enabled && from != to };
    }

    /// <summary>Az adott helyi órára esik-e a tiltás.</summary>
    public bool Covers(int localHour) =>
        Enabled && (From < To ? localHour >= From && localHour < To : localHour >= From || localHour < To);
}

/// <summary>Az adminisztrátor által módosítható korlátok (limits.json; a telepítő tölti ki).</summary>
public sealed record LimitSettings
{
    public ResetMode ResetMode { get; init; } = ResetMode.Rolling;

    /// <summary>Napi visszaállítás órája (0–23, helyi idő), csak <see cref="ResetMode.Daily"/> esetén.</summary>
    public int DailyResetHour { get; init; } = 3;

    public int LolPvpMatches { get; init; } = 3;
    public int ShortVideoMinutes { get; init; } = 60;
    public int FacebookFeedMinutes { get; init; } = 60;

    /// <summary>Hétvégén (szombat, vasárnap) is érvényesek-e a korlátok. Alapértelmezés: nem.</summary>
    public bool EnforceOnWeekends { get; init; }

    // Pihenőnap-jutalom: ha az előző korlátozott napon a keretből semmi sem fogyott,
    // a következő napra ennyivel nagyobb a keret. Nem halmozódik (több üres nap után is csak egyszer jár).
    public int LolRestDayBonusMatches { get; init; } = 1;
    public int ShortVideoRestDayBonusMinutes { get; init; } = 30;
    public int FacebookFeedRestDayBonusMinutes { get; init; } = 30;

    // Tiltott órák keretenként (alapértelmezés: nincs).
    public BannedHours LolBannedHours { get; init; } = BannedHours.Disabled;
    public BannedHours ShortVideoBannedHours { get; init; } = BannedHours.Disabled;
    public BannedHours FacebookFeedBannedHours { get; init; } = BannedHours.Disabled;

    public BannedHours WebBannedHours(WebCategory category) => category switch
    {
        WebCategory.ShortVideo => ShortVideoBannedHours,
        WebCategory.FacebookFeed => FacebookFeedBannedHours,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static LimitSettings Default { get; } = new();

    /// <summary>A limits.json formátuma: camelCase mezők, "resetMode": "rolling" | "daily".</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static LimitSettings Parse(string json) =>
        (JsonSerializer.Deserialize<LimitSettings>(json, JsonOptions) ?? throw new JsonException("üres fájl")).Sanitized();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public TimeSpan WebLimit(WebCategory category) => TimeSpan.FromMinutes(category switch
    {
        WebCategory.ShortVideo => ShortVideoMinutes,
        WebCategory.FacebookFeed => FacebookFeedMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    });

    public TimeSpan WebRestDayBonus(WebCategory category) => TimeSpan.FromMinutes(category switch
    {
        WebCategory.ShortVideo => ShortVideoRestDayBonusMinutes,
        WebCategory.FacebookFeed => FacebookFeedRestDayBonusMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    });

    /// <summary>Jutalom nélküli változat (pl. tesztekhez).</summary>
    public LimitSettings WithoutRestDayBonus() => this with
    {
        LolRestDayBonusMatches = 0,
        ShortVideoRestDayBonusMinutes = 0,
        FacebookFeedRestDayBonusMinutes = 0,
    };

    public LimitSettings Sanitized() => this with
    {
        ResetMode = Enum.IsDefined(ResetMode) ? ResetMode : ResetMode.Rolling,
        DailyResetHour = Math.Clamp(DailyResetHour, 0, 23),
        LolPvpMatches = Math.Clamp(LolPvpMatches, 0, 1000),
        ShortVideoMinutes = Math.Clamp(ShortVideoMinutes, 0, 24 * 60),
        FacebookFeedMinutes = Math.Clamp(FacebookFeedMinutes, 0, 24 * 60),
        LolRestDayBonusMatches = Math.Clamp(LolRestDayBonusMatches, 0, 10),
        ShortVideoRestDayBonusMinutes = Math.Clamp(ShortVideoRestDayBonusMinutes, 0, 24 * 60),
        FacebookFeedRestDayBonusMinutes = Math.Clamp(FacebookFeedRestDayBonusMinutes, 0, 24 * 60),
        LolBannedHours = (LolBannedHours ?? BannedHours.Disabled).Sanitized(),
        ShortVideoBannedHours = (ShortVideoBannedHours ?? BannedHours.Disabled).Sanitized(),
        FacebookFeedBannedHours = (FacebookFeedBannedHours ?? BannedHours.Disabled).Sanitized(),
    };
}
