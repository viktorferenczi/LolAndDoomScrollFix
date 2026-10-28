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
    /// Whether a banned period is active now, and if so, until when. There is no ban in an exempt period (weekend).
    /// </summary>
    public (bool Active, DateTimeOffset? Until) Ban(BannedHours ban, DateTimeOffset now)
    {
        if (!ban.Enabled || IsExempt(now)) return (false, null);
        var local = TimeZoneInfo.ConvertTime(now, _zone);
        if (!ban.Covers(local.Hour)) return (false, null);
        var end = local.Date.AddHours(ban.To);
        if (end <= local.DateTime) end = end.AddDays(1);
        return (true, ToUtc(end));
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
