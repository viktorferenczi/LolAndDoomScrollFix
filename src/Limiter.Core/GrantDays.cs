namespace Limiter.Core;

public enum GrantDayStatus
{
    /// <summary>Kiválasztható.</summary>
    Ok = 0,

    /// <summary>Hétvége, és a hétvégi korlát ki van kapcsolva: ilyenkor úgysincs korlát, az extra felesleges.</summary>
    ExemptWeekend = 1,
}

/// <param name="Date">A nap kezdő dátuma.</param>
/// <param name="IsToday">A most tartó nap (napi módban a visszaállítás órája előtt még az előző naptári napé).</param>
/// <param name="HasGrant">Van már rá extra keret.</param>
public sealed record GrantDayOption(DateOnly Date, GrantDayStatus Status, bool IsToday, bool HasGrant)
{
    public bool Selectable => Status == GrantDayStatus.Ok;
}

/// <summary>
/// Az extra keret napválasztójának szabályai: mely napok választhatók, és melyek nem (és miért).
/// A múltbeli napok nem szerepelnek (nincs mit adni nekik); a lista a mostani naptól <see cref="HorizonDays"/> napra előre tart.
/// </summary>
public static class GrantDays
{
    public const int HorizonDays = 30;

    /// <summary>A most tartó nap kezdő dátuma a megadott beállításokkal (napi módban a visszaállítás órájától függ).</summary>
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

    /// <summary>A lejárt (a mostani napnál korábbi) extrák nélküli beállítások.</summary>
    public static LimitSettings Pruned(LimitSettings settings, DateTimeOffset now, TimeZoneInfo zone) =>
        settings.WithoutGrantsBefore(Today(settings, now, zone));
}
