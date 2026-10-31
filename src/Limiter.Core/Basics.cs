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

/// <summary>The web time limits. A second always uses up at most one of them.</summary>
public enum WebCategory
{
    /// <summary>Instagram Reels, TikTok, YouTube Shorts and Facebook Reels — one shared limit.</summary>
    ShortVideo = 1,

    /// <summary>The Facebook home feed — a separate limit.</summary>
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

/// <summary>When the used limit is released.</summary>
public enum ResetMode
{
    /// <summary>Rolling 24 hours: every use is released exactly 24 hours later.</summary>
    Rolling = 0,

    /// <summary>Once a day, at the given hour, every limit resets at once.</summary>
    Daily = 1,
}

/// <summary>
/// A banned period within a day (local time, whole hours): from <see cref="From"/> (inclusive) to <see cref="To"/> (exclusive).
/// It may span midnight (e.g. 22 → 6), and 0 → 24 is the whole day. It does not affect the limit or the reset; it only makes use impossible meanwhile.
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
        var to = Math.Clamp(To, 0, 24);
        return this with { From = from, To = to, Enabled = Enabled && from != to };
    }

    /// <summary>Whether the ban covers the given local hour.</summary>
    public bool Covers(int localHour) =>
        Enabled && (From < To ? localHour >= From && localHour < To : localHour >= From || localHour < To);
}

/// <summary>
/// Banned hours per weekday. A period belongs to the calendar day it starts on: Monday 22 → 6 lasts until Tuesday 06:00.
/// </summary>
public sealed record WeeklyBan
{
    public BannedHours Monday { get; init; } = BannedHours.Disabled;
    public BannedHours Tuesday { get; init; } = BannedHours.Disabled;
    public BannedHours Wednesday { get; init; } = BannedHours.Disabled;
    public BannedHours Thursday { get; init; } = BannedHours.Disabled;
    public BannedHours Friday { get; init; } = BannedHours.Disabled;
    public BannedHours Saturday { get; init; } = BannedHours.Disabled;
    public BannedHours Sunday { get; init; } = BannedHours.Disabled;

    public static WeeklyBan Disabled { get; } = new();

    /// <summary>The days in the order they are shown: Monday first.</summary>
    public static IReadOnlyList<DayOfWeek> Days { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    [JsonIgnore]
    public bool AnyEnabled => Days.Any(d => For(d).Enabled);

    public BannedHours For(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };

    public WeeklyBan With(DayOfWeek day, BannedHours hours) => day switch
    {
        DayOfWeek.Monday => this with { Monday = hours },
        DayOfWeek.Tuesday => this with { Tuesday = hours },
        DayOfWeek.Wednesday => this with { Wednesday = hours },
        DayOfWeek.Thursday => this with { Thursday = hours },
        DayOfWeek.Friday => this with { Friday = hours },
        DayOfWeek.Saturday => this with { Saturday = hours },
        DayOfWeek.Sunday => this with { Sunday = hours },
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };

    /// <summary>
    /// A single period from a file of an earlier version: every day, but the weekend only if the limits applied on weekends,
    /// because earlier a free weekend had no ban either.
    /// </summary>
    public static WeeklyBan FromLegacy(BannedHours? hours, bool enforceOnWeekends)
    {
        if (hours is not { Enabled: true }) return Disabled;
        var ban = Disabled;
        foreach (var day in Days)
        {
            if (enforceOnWeekends || day is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) ban = ban.With(day, hours);
        }
        return ban;
    }

    public WeeklyBan Sanitized() => this with
    {
        Monday = (Monday ?? BannedHours.Disabled).Sanitized(),
        Tuesday = (Tuesday ?? BannedHours.Disabled).Sanitized(),
        Wednesday = (Wednesday ?? BannedHours.Disabled).Sanitized(),
        Thursday = (Thursday ?? BannedHours.Disabled).Sanitized(),
        Friday = (Friday ?? BannedHours.Disabled).Sanitized(),
        Saturday = (Saturday ?? BannedHours.Disabled).Sanitized(),
        Sunday = (Sunday ?? BannedHours.Disabled).Sanitized(),
    };
}

/// <summary>
/// An extra allowance given by the administrator for a chosen day (by the day's "start date": in daily mode from the
/// reset hour to the next one, in rolling mode from midnight to midnight). It is added to the base limit and the rest-day bonus.
/// </summary>
public sealed record DayGrant
{
    public const int MaxMatches = 100;
    public const int MaxMinutes = 24 * 60;

    public DateOnly Date { get; init; }
    public int LolMatches { get; init; }
    public int ShortVideoMinutes { get; init; }
    public int FacebookFeedMinutes { get; init; }

    /// <summary>Contains nothing (such an entry is not stored).</summary>
    [JsonIgnore]
    public bool IsEmpty => LolMatches <= 0 && ShortVideoMinutes <= 0 && FacebookFeedMinutes <= 0;

    public DayGrant Sanitized() => this with
    {
        LolMatches = Math.Clamp(LolMatches, 0, MaxMatches),
        ShortVideoMinutes = Math.Clamp(ShortVideoMinutes, 0, MaxMinutes),
        FacebookFeedMinutes = Math.Clamp(FacebookFeedMinutes, 0, MaxMinutes),
    };
}

/// <summary>The limits the administrator can change (limits.json; filled in by the installer).</summary>
public sealed record LimitSettings
{
    public ResetMode ResetMode { get; init; } = ResetMode.Rolling;

    /// <summary>Hour of the daily reset (0–23, local time), only for <see cref="ResetMode.Daily"/>.</summary>
    public int DailyResetHour { get; init; } = 3;

    public int LolPvpMatches { get; init; } = 3;
    public int ShortVideoMinutes { get; init; } = 60;
    public int FacebookFeedMinutes { get; init; } = 60;

    /// <summary>Whether the limits also apply on weekends (Saturday, Sunday). Default: no.</summary>
    public bool EnforceOnWeekends { get; init; }

    // Rest-day bonus: if nothing was used from the limit on the previous limited day,
    // the limit is larger by this much for the next day. It does not stack (it is given only once even after several empty days).
    public int LolRestDayBonusMatches { get; init; } = 1;
    public int ShortVideoRestDayBonusMinutes { get; init; } = 30;
    public int FacebookFeedRestDayBonusMinutes { get; init; } = 30;

    // Banned hours per limit and weekday (default: none). A day's ban also applies on a weekend without limits.
    public WeeklyBan LolBannedDays { get; init; } = WeeklyBan.Disabled;
    public WeeklyBan ShortVideoBannedDays { get; init; } = WeeklyBan.Disabled;
    public WeeklyBan FacebookFeedBannedDays { get; init; } = WeeklyBan.Disabled;

    // The single period of earlier versions: only read from an old file, Sanitized converts it to the weekly form.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BannedHours? LolBannedHours { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BannedHours? ShortVideoBannedHours { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BannedHours? FacebookFeedBannedHours { get; init; }

    /// <summary>Extra allowances for single days (at most one entry per day, sorted by date).</summary>
    public IReadOnlyList<DayGrant> ExtraGrants { get; init; } = [];

    public const int MaxGrants = 400;

    // The record's default equality would compare the list by reference, so it is written by hand.
    // When a new field is added, it has to be added here too (LimitSettingsEqualityTests checks it).
    public bool Equals(LimitSettings? other) =>
        other is not null &&
        ResetMode == other.ResetMode &&
        DailyResetHour == other.DailyResetHour &&
        LolPvpMatches == other.LolPvpMatches &&
        ShortVideoMinutes == other.ShortVideoMinutes &&
        FacebookFeedMinutes == other.FacebookFeedMinutes &&
        EnforceOnWeekends == other.EnforceOnWeekends &&
        LolRestDayBonusMatches == other.LolRestDayBonusMatches &&
        ShortVideoRestDayBonusMinutes == other.ShortVideoRestDayBonusMinutes &&
        FacebookFeedRestDayBonusMinutes == other.FacebookFeedRestDayBonusMinutes &&
        Equals(LolBannedDays, other.LolBannedDays) &&
        Equals(ShortVideoBannedDays, other.ShortVideoBannedDays) &&
        Equals(FacebookFeedBannedDays, other.FacebookFeedBannedDays) &&
        Equals(LolBannedHours, other.LolBannedHours) &&
        Equals(ShortVideoBannedHours, other.ShortVideoBannedHours) &&
        Equals(FacebookFeedBannedHours, other.FacebookFeedBannedHours) &&
        (ExtraGrants ?? []).SequenceEqual(other.ExtraGrants ?? []);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ResetMode);
        hash.Add(DailyResetHour);
        hash.Add(LolPvpMatches);
        hash.Add(ShortVideoMinutes);
        hash.Add(FacebookFeedMinutes);
        hash.Add(EnforceOnWeekends);
        hash.Add(LolRestDayBonusMatches);
        hash.Add(ShortVideoRestDayBonusMinutes);
        hash.Add(FacebookFeedRestDayBonusMinutes);
        hash.Add(LolBannedDays);
        hash.Add(ShortVideoBannedDays);
        hash.Add(FacebookFeedBannedDays);
        hash.Add(LolBannedHours);
        hash.Add(ShortVideoBannedHours);
        hash.Add(FacebookFeedBannedHours);
        foreach (var g in ExtraGrants ?? []) hash.Add(g);
        return hash.ToHashCode();
    }

    public DayGrant? GrantFor(DateOnly day) => ExtraGrants?.FirstOrDefault(g => g.Date == day);

    public int ExtraMatches(DateOnly day) => GrantFor(day)?.LolMatches ?? 0;

    public TimeSpan ExtraWebTime(WebCategory category, DateOnly day) => TimeSpan.FromMinutes(category switch
    {
        WebCategory.ShortVideo => GrantFor(day)?.ShortVideoMinutes ?? 0,
        WebCategory.FacebookFeed => GrantFor(day)?.FacebookFeedMinutes ?? 0,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    });

    /// <summary>Sets (or overwrites) the day's extra allowance; an empty entry removes it.</summary>
    public LimitSettings WithGrant(DayGrant grant) => this with
    {
        ExtraGrants = (ExtraGrants ?? []).Where(g => g.Date != grant.Date)
            .Concat(grant.IsEmpty ? [] : [grant]).OrderBy(g => g.Date).ToList(),
    };

    public LimitSettings WithoutGrant(DateOnly day) => this with
    {
        ExtraGrants = (ExtraGrants ?? []).Where(g => g.Date != day).ToList(),
    };

    /// <summary>Deletes the extras before the given day (already expired, no effect).</summary>
    public LimitSettings WithoutGrantsBefore(DateOnly day) => this with
    {
        ExtraGrants = (ExtraGrants ?? []).Where(g => g.Date >= day).ToList(),
    };

    public WeeklyBan LolBan() => Effective(LolBannedDays, LolBannedHours);

    public WeeklyBan WebBan(WebCategory category) => category switch
    {
        WebCategory.ShortVideo => Effective(ShortVideoBannedDays, ShortVideoBannedHours),
        WebCategory.FacebookFeed => Effective(FacebookFeedBannedDays, FacebookFeedBannedHours),
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private WeeklyBan Effective(WeeklyBan? days, BannedHours? legacy) =>
        days is { AnyEnabled: true } ? days : WeeklyBan.FromLegacy(legacy, EnforceOnWeekends);

    public static LimitSettings Default { get; } = new();

    /// <summary>The format of limits.json: camelCase fields, "resetMode": "rolling" | "daily".</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static LimitSettings Parse(string json) =>
        (JsonSerializer.Deserialize<LimitSettings>(json, JsonOptions) ?? throw new JsonException("empty file")).Sanitized();

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

    /// <summary>A variant without bonus (e.g. for tests).</summary>
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
        LolBannedDays = Effective(LolBannedDays, LolBannedHours?.Sanitized()).Sanitized(),
        ShortVideoBannedDays = Effective(ShortVideoBannedDays, ShortVideoBannedHours?.Sanitized()).Sanitized(),
        FacebookFeedBannedDays = Effective(FacebookFeedBannedDays, FacebookFeedBannedHours?.Sanitized()).Sanitized(),
        LolBannedHours = null,
        ShortVideoBannedHours = null,
        FacebookFeedBannedHours = null,
        ExtraGrants = (ExtraGrants ?? [])
            .Where(g => g is not null && g.Date != default)
            .Select(g => g.Sanitized())
            .Where(g => !g.IsEmpty)
            .GroupBy(g => g.Date)
            .Select(group => group.Last())
            .OrderBy(g => g.Date)
            .Take(MaxGrants)
            .ToList(),
    };
}
