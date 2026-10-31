namespace Limiter.Core;

/// <summary>
/// Schedule: from when the limit counts (rolling 24 hours or since the daily reset), and when the weekend is exempt.
/// In daily mode a "day" lasts from the reset hour to the next daily reset (e.g. 03:00–03:00),
/// in rolling mode from midnight to midnight. Weekend: a day that starts on Saturday or Sunday.
/// </summary>
public sealed class LimitSchedule
{
    private readonly LimitSettings _settings;
    private readonly TimeZoneInfo _zone;

    public LimitSchedule(LimitSettings settings, TimeZoneInfo zone)
    {
        _settings = settings;
        _zone = zone;
    }

    public ResetMode Mode => _settings.ResetMode;

    private int DayStartHour => _settings.ResetMode == ResetMode.Daily ? _settings.DailyResetHour : 0;

    /// <summary>The start date of the day containing <paramref name="now"/> (local date).</summary>
    public DateTime LogicalDate(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, _zone);
        return local.TimeOfDay < TimeSpan.FromHours(DayStartHour) ? local.Date.AddDays(-1) : local.Date;
    }

    /// <summary>The start date of the day containing <paramref name="now"/> (extra allowances are tied to it).</summary>
    public DateOnly LogicalDay(DateTimeOffset now) => DateOnly.FromDateTime(LogicalDate(now));

    /// <summary>Whether the given day is exempt (weekend, if the weekend limit is not on).</summary>
    public bool IsExemptDay(DateOnly day) => !_settings.EnforceOnWeekends && IsWeekend(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>The start of the current day (in daily mode: the last reset).</summary>
    public DateTimeOffset DayStart(DateTimeOffset now) => ToUtc(LogicalDate(now).AddHours(DayStartHour));

    /// <summary>The start of the next day (in daily mode: the next reset).</summary>
    public DateTimeOffset NextDayStart(DateTimeOffset now) => ToUtc(LogicalDate(now).AddDays(1).AddHours(DayStartHour));

    /// <summary>The start of the period that counts toward the limit.</summary>
    public DateTimeOffset WindowStart(DateTimeOffset now) =>
        _settings.ResetMode == ResetMode.Daily ? DayStart(now) : now - LimitEngine.Window;

    /// <summary>Whether we are exempt from the restriction now (weekend, if the weekend limit is not on).</summary>
    public bool IsExempt(DateTimeOffset now) =>
        !_settings.EnforceOnWeekends && IsWeekend(LogicalDate(now));

    /// <summary>The start of the next exempt (weekend) period, or null if there is a limit on weekends too.</summary>
    public DateTimeOffset? NextExemptStart(DateTimeOffset now)
    {
        if (_settings.EnforceOnWeekends) return null;
        if (IsExempt(now)) return now;
        var date = LogicalDate(now);
        do date = date.AddDays(1); while (!IsWeekend(date));
        return ToUtc(date.AddHours(DayStartHour));
    }

    /// <summary>The bounds of today (in daily mode: between two resets, in rolling mode: from midnight to midnight).</summary>
    public (DateTimeOffset Start, DateTimeOffset End) Today(DateTimeOffset now) => (DayStart(now), NextDayStart(now));

    /// <summary>
    /// The most recent limited day before today. With an exempt weekend it skips the weekend
    /// (on Monday Friday counts), because no usage is recorded on weekends.
    /// </summary>
    public (DateTimeOffset Start, DateTimeOffset End)? PreviousEnforcedDay(DateTimeOffset now)
    {
        var date = LogicalDate(now);
        for (var i = 0; i < 7; i++)
        {
            date = date.AddDays(-1);
            if (_settings.EnforceOnWeekends || !IsWeekend(date))
                return (ToUtc(date.AddHours(DayStartHour)), ToUtc(date.AddDays(1).AddHours(DayStartHour)));
        }
        return null;
    }

    /// <summary>
    /// Whether a banned period is active now, and if so, until when (adjoining periods of the next days count as one;
    /// null if it does not end within a week). The days are calendar days, also with a daily reset, and an exempt weekend has its own ban too.
    /// </summary>
    public (bool Active, DateTimeOffset? Until) Ban(WeeklyBan ban, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, _zone).DateTime;
        if (Covering(ban, local) is null) return (false, null);
        var hour = local.Date.AddHours(local.Hour);
        for (var i = 0; i < 8 * 24; i++)
        {
            hour = hour.AddHours(1);
            if (Covering(ban, hour) is null) return (true, ToUtc(hour));
        }
        return (true, null);
    }

    /// <summary>The period to show now: the one in effect, otherwise today's.</summary>
    public BannedHours CurrentOrToday(WeeklyBan ban, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, _zone).DateTime;
        return Covering(ban, local) ?? ban.For(local.DayOfWeek);
    }

    /// <summary>The period covering the given local time: today's, or yesterday's if that spans midnight.</summary>
    private static BannedHours? Covering(WeeklyBan ban, DateTime local)
    {
        var today = ban.For(local.DayOfWeek);
        if (today.Enabled && today.From != today.To && local.Hour >= today.From && (today.From > today.To || local.Hour < today.To))
            return today;
        var yesterday = ban.For(local.AddDays(-1).DayOfWeek);
        return yesterday.Enabled && yesterday.From > yesterday.To && local.Hour < yesterday.To ? yesterday : null;
    }

    private static bool IsWeekend(DateTime date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private DateTimeOffset ToUtc(DateTime localWallClock)
    {
        var local = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        // When daylight saving time starts, the skipped hour does not exist: then one hour later.
        if (_zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _zone), TimeSpan.Zero);
    }
}
