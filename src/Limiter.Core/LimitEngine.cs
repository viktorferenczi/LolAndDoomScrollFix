namespace Limiter.Core;

public sealed record WebLimitStatus(
    WebCategory Category,
    TimeSpan Limit,
    TimeSpan Used,
    TimeSpan Remaining,
    bool Blocked,
    DateTimeOffset? NextReleaseAt,
    DateTimeOffset? AvailableAgainAt,
    bool Exempt = false,
    TimeSpan Bonus = default,
    bool Banned = false,
    TimeSpan Extra = default);

public sealed record LolLimitStatus(
    int Limit,
    int Used,
    int Remaining,
    bool Blocked,
    DateTimeOffset? NextReleaseAt,
    DateTimeOffset? AvailableAgainAt,
    bool Exempt = false,
    int Bonus = 0,
    bool Banned = false,
    int Extra = 0);

/// <summary>
/// A keretek számítása. Gördülő módban nincs éjféli nullázás: minden felhasználás pontosan
/// 24 órával később szabadul fel (a webes idő percenként, fokozatosan). Napi módban a megadott órában
/// minden keret egyszerre nullázódik. Mentes időszakban (hétvége) semmi sem tilt és semmi sem fogy.
/// Pihenőnap-jutalom: ha az előző korlátozott napon egy keretből semmi sem fogyott, ma ennyivel nagyobb
/// (nem halmozódik; 0-s alapkeretnél nem jár). Az admin által egy napra adott extra keret (<see cref="DayGrant"/>) ehhez adódik, a nap
/// kezdő dátuma szerint; mentes napon nincs hatása. Tiltott órákban a keret nem fogy és nem nullázódik, csak tilt.
/// </summary>
public sealed class LimitEngine
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>Egy jelentés legfeljebb ennyi időt számolhat el (védelem a hibás/túlzó jelentések ellen).</summary>
    public static readonly TimeSpan MaxClaim = TimeSpan.FromMinutes(5);

    private readonly UsageStore _store;
    private readonly Func<LimitSettings> _settings;
    private readonly IClock _clock;
    private readonly TimeZoneInfo _zone;
    private readonly Dictionary<(string Sid, WebCategory Category), DateTimeOffset> _lastReportAt = [];
    private readonly Lock _gate = new();

    public LimitEngine(UsageStore store, Func<LimitSettings> settings, IClock clock, TimeZoneInfo? zone = null)
    {
        _store = store;
        _settings = settings;
        _clock = clock;
        _zone = zone ?? TimeZoneInfo.Local;
        TrackingStart = store.EnsureTrackingStart(clock.UtcNow);
    }

    public LimitSettings Settings => _settings();

    /// <summary>Mióta méri a program a használatot. Pihenőnap-jutalom csak egy ennél későbbi kezdetű, teljes napra jár.</summary>
    public DateTimeOffset TrackingStart { get; }

    public LimitSchedule Schedule => new(_settings(), _zone);

    /// <summary>Most mentes-e a korlátozás alól (hétvége, ha nincs hétvégi korlát).</summary>
    public bool IsExemptNow => Schedule.IsExempt(_clock.UtcNow);

    /// <summary>
    /// Webes használat elszámolása. Ha több Chrome-profil egyszerre jelent ugyanarra a keretre, az átfedő
    /// időt csak egyszer számolja: egy jelentés legfeljebb az előző jelentés óta eltelt időt fogadja el.
    /// Mentes időszakban semmit sem számol.
    /// </summary>
    /// <returns>Az elfogadott időtartam.</returns>
    public TimeSpan ReportWebUsage(string userSid, WebCategory category, TimeSpan claimed)
    {
        if (claimed <= TimeSpan.Zero) return TimeSpan.Zero;
        if (claimed > MaxClaim) claimed = MaxClaim;

        var now = _clock.UtcNow;
        TimeSpan accepted;
        lock (_gate)
        {
            accepted = claimed;
            if (_lastReportAt.TryGetValue((userSid, category), out var last))
            {
                var sinceLast = now - last;
                if (sinceLast < TimeSpan.Zero) sinceLast = TimeSpan.Zero;
                if (sinceLast < accepted) accepted = sinceLast;
            }
            _lastReportAt[(userSid, category)] = now;
        }

        if (Schedule.IsExempt(now)) return TimeSpan.Zero;

        var ms = (long)accepted.TotalMilliseconds;
        if (ms > 0) _store.AddWebUsage(userSid, category, now, ms);
        return TimeSpan.FromMilliseconds(ms);
    }

    public WebLimitStatus GetWebStatus(string userSid, WebCategory category)
    {
        var now = _clock.UtcNow;
        var settings = _settings();
        var schedule = new LimitSchedule(settings, _zone);
        var baseLimit = settings.WebLimit(category);
        var bonus = WebBonusAfter(userSid, category, settings, schedule.PreviousEnforcedDay(now));
        var extra = ExtraWeb(settings, schedule, category, schedule.LogicalDay(now));
        var limit = baseLimit + bonus + extra;
        var buckets = _store.GetWebBuckets(userSid, category, schedule.WindowStart(now));
        var used = TimeSpan.FromMilliseconds(buckets.Sum(b => b.Milliseconds));

        if (schedule.IsExempt(now))
            return new WebLimitStatus(category, limit, used, limit, false, null, null, Exempt: true, Bonus: bonus, Extra: extra);

        var remaining = used >= limit ? TimeSpan.Zero : limit - used;
        var blocked = used >= limit;
        DateTimeOffset? next, availableAgain = null;

        if (schedule.Mode == ResetMode.Daily)
        {
            var reset = schedule.NextDayStart(now);
            next = used > TimeSpan.Zero ? reset : null;
            if (blocked) availableAgain = reset;
        }
        else
        {
            next = buckets.Count > 0 ? buckets[0].ReleasedAt : null;
            if (blocked)
            {
                // Holnaptól a keret változhat: a mai jutalom megszűnik, illetve ha ma semmi sem fogyott, holnap jár.
                var today = schedule.Today(now);
                var tomorrowLimit = baseLimit + WebBonusAfter(userSid, category, settings, today)
                                    + ExtraWeb(settings, schedule, category, schedule.LogicalDay(now).AddDays(1));
                availableAgain = FirstMomentBelowLimit(now, today.End,
                    buckets.Select(b => (b.ReleasedAt, (double)b.Milliseconds)).ToList(),
                    limit.TotalMilliseconds, tomorrowLimit.TotalMilliseconds);
            }
        }

        var banned = ApplyBan(schedule.Ban(settings.WebBannedHours(category), now), ref blocked, ref availableAgain);
        if (blocked) availableAgain = EarlierOfExempt(schedule, now, availableAgain);
        return new WebLimitStatus(category, limit, used, remaining, blocked, next, availableAgain, Bonus: bonus, Banned: banned, Extra: extra);
    }

    public LolLimitStatus GetLolStatus(string userSid)
    {
        var now = _clock.UtcNow;
        var settings = _settings();
        var schedule = new LimitSchedule(settings, _zone);
        var baseLimit = settings.LolPvpMatches;
        var bonus = LolBonusAfter(userSid, settings, schedule.PreviousEnforcedDay(now));
        var extra = ExtraLol(settings, schedule, schedule.LogicalDay(now));
        var limit = baseLimit + bonus + extra;
        // Gördülő módban a pontosan 24 órája indult meccs már felszabadult; napi módban a nullázáskor indult már számít.
        var windowStart = schedule.WindowStart(now);
        var since = schedule.Mode == ResetMode.Daily ? windowStart - TimeSpan.FromMilliseconds(1) : windowStart;
        var counted = _store.GetMatchesSince(userSid, since)
            .Where(m => m.RemakeStatus is not (RemakeStatus.Remake or RemakeStatus.Exempt))
            .ToList();
        var used = counted.Count;

        if (schedule.IsExempt(now))
            return new LolLimitStatus(limit, used, limit, false, null, null, Exempt: true, Bonus: bonus, Extra: extra);

        var blocked = used >= limit;
        DateTimeOffset? next, availableAgain = null;

        if (schedule.Mode == ResetMode.Daily)
        {
            var reset = schedule.NextDayStart(now);
            next = used > 0 ? reset : null;
            if (blocked) availableAgain = reset;
        }
        else
        {
            next = counted.Count > 0 ? counted[0].StartedAt + Window : null;
            if (blocked)
            {
                var today = schedule.Today(now);
                var tomorrowLimit = baseLimit + LolBonusAfter(userSid, settings, today)
                                    + ExtraLol(settings, schedule, schedule.LogicalDay(now).AddDays(1));
                availableAgain = FirstMomentBelowLimit(now, today.End,
                    counted.Select(m => (m.StartedAt + Window, 1.0)).ToList(),
                    limit, tomorrowLimit);
            }
        }

        var banned = ApplyBan(schedule.Ban(settings.LolBannedHours, now), ref blocked, ref availableAgain);
        if (blocked) availableAgain = EarlierOfExempt(schedule, now, availableAgain);
        return new LolLimitStatus(limit, used, Math.Max(0, limit - used), blocked, next, availableAgain, Bonus: bonus, Banned: banned, Extra: extra);
    }

    /// <summary>Az admin által a napra adott extra (mentes napon nincs hatása).</summary>
    private static TimeSpan ExtraWeb(LimitSettings settings, LimitSchedule schedule, WebCategory category, DateOnly day) =>
        schedule.IsExemptDay(day) ? TimeSpan.Zero : settings.ExtraWebTime(category, day);

    private static int ExtraLol(LimitSettings settings, LimitSchedule schedule, DateOnly day) =>
        schedule.IsExemptDay(day) ? 0 : settings.ExtraMatches(day);

    /// <summary>Jár-e jutalom a <paramref name="day"/> utáni napra (semmi sem fogyott azon a napon).</summary>
    private TimeSpan WebBonusAfter(string sid, WebCategory category, LimitSettings settings, (DateTimeOffset Start, DateTimeOffset End)? day)
    {
        var bonus = settings.WebRestDayBonus(category);
        if (day is not { } d || d.Start < TrackingStart) return TimeSpan.Zero; // nem teljesen mért nap
        if (bonus <= TimeSpan.Zero || settings.WebLimit(category) <= TimeSpan.Zero) return TimeSpan.Zero;
        return _store.SumWebUsage(sid, category, d.Start, d.End) == 0 ? bonus : TimeSpan.Zero;
    }

    private int LolBonusAfter(string sid, LimitSettings settings, (DateTimeOffset Start, DateTimeOffset End)? day)
    {
        var bonus = settings.LolRestDayBonusMatches;
        if (day is not { } d || d.Start < TrackingStart) return 0; // nem teljesen mért nap
        if (bonus <= 0 || settings.LolPvpMatches <= 0) return 0;
        return _store.CountCountedMatches(sid, d.Start, d.End) == 0 ? bonus : 0;
    }

    /// <summary>
    /// Gördülő mód: az első időpont, amikor a még ablakban lévő felhasználás a keret alá esik.
    /// Jelöltek: a tételek felszabadulása és a holnapi nap kezdete (ahol a keret megváltozhat).
    /// </summary>
    private static DateTimeOffset? FirstMomentBelowLimit(DateTimeOffset now, DateTimeOffset tomorrowStart,
        IReadOnlyList<(DateTimeOffset ReleasedAt, double Amount)> items, double todayLimit, double tomorrowLimit)
    {
        var candidates = items.Select(i => i.ReleasedAt).Append(tomorrowStart).Where(t => t > now).Distinct().Order();
        foreach (var t in candidates)
        {
            var usedAt = items.Where(i => i.ReleasedAt > t).Sum(i => i.Amount);
            if (usedAt < (t < tomorrowStart ? todayLimit : tomorrowLimit)) return t;
        }
        return null;
    }

    /// <summary>
    /// Tiltott időszakban tilt; a feloldás a tiltás vége, vagy ha a keret addig sem szabadul fel, a keret feloldása.
    /// </summary>
    private static bool ApplyBan((bool Active, DateTimeOffset? Until) ban, ref bool blocked, ref DateTimeOffset? availableAgain)
    {
        if (!ban.Active) return false;
        var until = ban.Until;
        if (blocked && (availableAgain is null || availableAgain > until)) until = availableAgain;
        blocked = true;
        availableAgain = until;
        return true;
    }

    /// <summary>Ha a hétvége hamarabb kezdődik, mint ahogy a keret felszabadul, akkor az a feloldás ideje.</summary>
    private static DateTimeOffset? EarlierOfExempt(LimitSchedule schedule, DateTimeOffset now, DateTimeOffset? availableAgain)
    {
        var exempt = schedule.NextExemptStart(now);
        if (exempt is null) return availableAgain;
        return availableAgain is null || exempt < availableAgain ? exempt : availableAgain;
    }
}
