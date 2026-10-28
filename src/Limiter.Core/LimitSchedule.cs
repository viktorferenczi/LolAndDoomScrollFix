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

    /// <summary>A <paramref name="now"/>-t tartalmazó nap kezdő dátuma (az extra keretek ehhez kötődnek).</summary>
    public DateOnly LogicalDay(DateTimeOffset now) => DateOnly.FromDateTime(LogicalDate(now));

    /// <summary>Mentes-e a megadott nap (hétvége, ha a hétvégi korlát nincs bekapcsolva).</summary>
    public bool IsExemptDay(DateOnly day) => !_settings.EnforceOnWeekends && IsWeekend(day.ToDateTime(TimeOnly.MinValue));

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

    /// <summary>A mai nap határai (napi módban: két visszaállítás között, gördülő módban: éjféltől éjfélig).</summary>
    public (DateTimeOffset Start, DateTimeOffset End) Today(DateTimeOffset now) => (DayStart(now), NextDayStart(now));

    /// <summary>
    /// A legutóbbi korlátozott nap a mai előtt. Mentes hétvégénél a hétvégét átugorja
    /// (hétfőn a péntek számít), mert hétvégén nem rögzül használat.
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
    /// Tart-e most a tiltott időszak, és ha igen, meddig. Mentes időszakban (hétvége) nincs tiltás.
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
        // Nyári időszámítás kezdetekor a kihagyott óra nem létezik: ilyenkor egy órával később.
        if (_zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _zone), TimeSpan.Zero);
    }
}
