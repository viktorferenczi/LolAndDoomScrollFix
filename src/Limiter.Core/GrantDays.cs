namespace Limiter.Core;

public enum GrantDayStatus
{
    /// <summary>Selectable.</summary>
    Ok = 0,

    /// <summary>Weekend with the weekend limit off: there is no limit then anyway, an extra would be pointless.</summary>
    ExemptWeekend = 1,
}

/// <param name="Date">The day's start date.</param>
/// <param name="IsToday">The current day (in daily mode before the reset hour it is still the previous calendar day's).</param>
/// <param name="HasGrant">It already has an extra allowance.</param>
public sealed record GrantDayOption(DateOnly Date, GrantDayStatus Status, bool IsToday, bool HasGrant)
{
    public bool Selectable => Status == GrantDayStatus.Ok;
}

/// <summary>
/// The rules of the extra-allowance day chooser: which days can be chosen and which cannot (and why).
/// Past days are not listed (nothing to give them); the list runs <see cref="HorizonDays"/> days ahead from the current day.
/// </summary>
public static class GrantDays
{
    public const int HorizonDays = 30;

    /// <summary>The start date of the current day with the given settings (in daily mode it depends on the reset hour).</summary>
    public static DateOnly Today(LimitSettings settings, DateTimeOffset now, TimeZoneInfo zone) =>
        new LimitSchedule(settings, zone).LogicalDay(now);

    public static GrantDayStatus Check(LimitSettings settings, DateOnly day, TimeZoneInfo zone) =>
        new LimitSchedule(settings, zone).IsExemptDay(day) ? GrantDayStatus.ExemptWeekend : GrantDayStatus.Ok;

    public static IReadOnlyList<GrantDayOption> Options(LimitSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        var schedule = new LimitSchedule(settings, zone);
        var today = schedule.LogicalDay(now);
        var list = new List<GrantDayOption>(HorizonDays + 1);
        for (var i = 0; i <= HorizonDays; i++)
        {
            var day = today.AddDays(i);
            list.Add(new GrantDayOption(
                day,
                schedule.IsExemptDay(day) ? GrantDayStatus.ExemptWeekend : GrantDayStatus.Ok,
                i == 0,
                settings.GrantFor(day) is not null));
        }
        return list;
    }

    /// <summary>The settings without the expired extras (those before the current day).</summary>
    public static LimitSettings Pruned(LimitSettings settings, DateTimeOffset now, TimeZoneInfo zone) =>
        settings.WithoutGrantsBefore(Today(settings, now, zone));
}
