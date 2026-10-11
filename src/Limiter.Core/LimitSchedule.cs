namespace Limiter.Core;

/// <summary>
/// Időbeosztás: honnan számít a keret (gördülő 24 óra vagy a napi visszaállítás óta), és mikor mentes a hétvége.
/// A „nap” napi módban a visszaállítás órájától tart a következő napi visszaállításig (pl. 03:00–03:00),
/// gördülő módban éjféltől éjfélig. Hétvége: az a nap, amelyik szombaton vagy vasárnap kezdődik.
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

    /// <summary>A <paramref name="now"/> időpontot tartalmazó nap kezdőnapja (helyi dátum).</summary>
    public DateTime LogicalDate(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, _zone);
        return local.TimeOfDay < TimeSpan.FromHours(DayStartHour) ? local.Date.AddDays(-1) : local.Date;
    }

    /// <summary>Az aktuális nap kezdete (napi módban: az utolsó visszaállítás).</summary>
    public DateTimeOffset DayStart(DateTimeOffset now) => ToUtc(LogicalDate(now).AddHours(DayStartHour));

    /// <summary>A következő nap kezdete (napi módban: a következő visszaállítás).</summary>
    public DateTimeOffset NextDayStart(DateTimeOffset now) => ToUtc(LogicalDate(now).AddDays(1).AddHours(DayStartHour));

    /// <summary>A keretbe számító időszak kezdete.</summary>
    public DateTimeOffset WindowStart(DateTimeOffset now) =>
        _settings.ResetMode == ResetMode.Daily ? DayStart(now) : now - LimitEngine.Window;

    /// <summary>Mentes-e most a korlátozás alól (hétvége, ha a hétvégi korlát nincs bekapcsolva).</summary>
    public bool IsExempt(DateTimeOffset now) =>
        !_settings.EnforceOnWeekends && IsWeekend(LogicalDate(now));

    /// <summary>A következő mentes (hétvégi) időszak kezdete, vagy null, ha a hétvégén is van korlát.</summary>
    public DateTimeOffset? NextExemptStart(DateTimeOffset now)
    {
        if (_settings.EnforceOnWeekends) return null;
        if (IsExempt(now)) return now;
        var date = LogicalDate(now);
        do date = date.AddDays(1); while (!IsWeekend(date));
        return ToUtc(date.AddHours(DayStartHour));
    }

    private static bool IsWeekend(DateTime date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private DateTimeOffset ToUtc(DateTime localWallClock)
    {
        var local = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        // Nyári időszámítás kezdetekor a kihagyott óra nem létezik: ilyenkor egy órával később.
        if (_zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _zone), TimeSpan.Zero);
    }
}
