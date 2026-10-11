namespace Limiter.Core;

public sealed record WebLimitStatus(
    WebCategory Category,
    TimeSpan Limit,
    TimeSpan Used,
    TimeSpan Remaining,
    bool Blocked,
    DateTimeOffset? NextReleaseAt,
    DateTimeOffset? AvailableAgainAt,
    bool Exempt = false);

public sealed record LolLimitStatus(
    int Limit,
    int Used,
    int Remaining,
    bool Blocked,
    DateTimeOffset? NextReleaseAt,
    DateTimeOffset? AvailableAgainAt,
    bool Exempt = false);

/// <summary>
/// A keretek számítása. Gördülő módban nincs éjféli nullázás: minden felhasználás pontosan
/// 24 órával később szabadul fel (a webes idő percenként, fokozatosan). Napi módban a megadott órában
/// minden keret egyszerre nullázódik. Mentes időszakban (hétvége) semmi sem tilt és semmi sem fogy.
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
    }

    public LimitSettings Settings => _settings();

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
        var schedule = Schedule;
        var limit = _settings().WebLimit(category);
        var buckets = _store.GetWebBuckets(userSid, category, schedule.WindowStart(now));
        var used = TimeSpan.FromMilliseconds(buckets.Sum(b => b.Milliseconds));

        if (schedule.IsExempt(now))
            return new WebLimitStatus(category, limit, used, limit, false, null, null, Exempt: true);

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
                // Annyi vödörnek kell kiesnie, hogy a felhasználás a keret alá menjen.
                var mustRelease = used - limit;
                var released = TimeSpan.Zero;
                foreach (var bucket in buckets)
                {
                    released += TimeSpan.FromMilliseconds(bucket.Milliseconds);
                    if (released > mustRelease)
                    {
                        availableAgain = bucket.ReleasedAt;
                        break;
                    }
                }
            }
        }

        if (blocked) availableAgain = EarlierOfExempt(schedule, now, availableAgain);
        return new WebLimitStatus(category, limit, used, remaining, blocked, next, availableAgain);
    }

    public LolLimitStatus GetLolStatus(string userSid)
    {
        var now = _clock.UtcNow;
        var schedule = Schedule;
        var limit = _settings().LolPvpMatches;
        var windowStart = schedule.WindowStart(now);
        var counted = _store.GetMatchesSince(userSid, windowStart - TimeSpan.FromMilliseconds(1))
            .Where(m => m.RemakeStatus is not (RemakeStatus.Remake or RemakeStatus.Exempt) && m.StartedAt >= windowStart)
            .ToList();
        var used = counted.Count;

        if (schedule.IsExempt(now))
            return new LolLimitStatus(limit, used, limit, false, null, null, Exempt: true);

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
            if (blocked && counted.Count > 0)
            {
                // used - limit + 1 meccsnek kell kiesnie ahhoz, hogy újra legyen szabad alkalom.
                var index = used - limit;
                availableAgain = counted[Math.Min(index, counted.Count - 1)].StartedAt + Window;
            }
        }

        if (blocked) availableAgain = EarlierOfExempt(schedule, now, availableAgain);
        return new LolLimitStatus(limit, used, Math.Max(0, limit - used), blocked, next, availableAgain);
    }

    /// <summary>Ha a hétvége hamarabb kezdődik, mint ahogy a keret felszabadul, akkor az a feloldás ideje.</summary>
    private static DateTimeOffset? EarlierOfExempt(LimitSchedule schedule, DateTimeOffset now, DateTimeOffset? availableAgain)
    {
        var exempt = schedule.NextExemptStart(now);
        if (exempt is null) return availableAgain;
        return availableAgain is null || exempt < availableAgain ? exempt : availableAgain;
    }
}
