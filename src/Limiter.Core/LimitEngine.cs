namespace Limiter.Core;

public sealed record WebLimitStatus(
    WebCategory Category,
    TimeSpan Limit,
    TimeSpan Used,
    TimeSpan Remaining,
    bool Blocked,
    DateTimeOffset? NextReleaseAt,
    DateTimeOffset? AvailableAgainAt);

public sealed record LolLimitStatus(
    int Limit,
    int Used,
    int Remaining,
    bool Blocked,
    DateTimeOffset? NextReleaseAt,
    DateTimeOffset? AvailableAgainAt);

/// <summary>
/// A gördülő 24 órás keretek számítása. Nincs éjféli nullázás: minden felhasználás pontosan
/// 24 órával később szabadul fel (a webes idő percenként, fokozatosan).
/// </summary>
public sealed class LimitEngine
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>Egy jelentés legfeljebb ennyi időt számolhat el (védelem a hibás/túlzó jelentések ellen).</summary>
    public static readonly TimeSpan MaxClaim = TimeSpan.FromMinutes(5);

    private readonly UsageStore _store;
    private readonly Func<LimitSettings> _settings;
    private readonly IClock _clock;
    private readonly Dictionary<(string Sid, WebCategory Category), DateTimeOffset> _lastReportAt = [];
    private readonly Lock _gate = new();

    public LimitEngine(UsageStore store, Func<LimitSettings> settings, IClock clock)
    {
        _store = store;
        _settings = settings;
        _clock = clock;
    }

    public LimitSettings Settings => _settings();

    /// <summary>
    /// Webes használat elszámolása. Ha több Chrome-profil egyszerre jelent ugyanarra a keretre, az átfedő
    /// időt csak egyszer számolja: egy jelentés legfeljebb az előző jelentés óta eltelt időt fogadja el.
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

        var ms = (long)accepted.TotalMilliseconds;
        if (ms > 0) _store.AddWebUsage(userSid, category, now, ms);
        return TimeSpan.FromMilliseconds(ms);
    }

    public WebLimitStatus GetWebStatus(string userSid, WebCategory category)
    {
        var now = _clock.UtcNow;
        var limit = _settings().WebLimit(category);
        var buckets = _store.GetWebBuckets(userSid, category, now - Window);
        var used = TimeSpan.FromMilliseconds(buckets.Sum(b => b.Milliseconds));
        var remaining = used >= limit ? TimeSpan.Zero : limit - used;
        var blocked = used >= limit;

        DateTimeOffset? next = buckets.Count > 0 ? buckets[0].ReleasedAt : null;
        DateTimeOffset? availableAgain = null;
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

        return new WebLimitStatus(category, limit, used, remaining, blocked, next, availableAgain);
    }

    public LolLimitStatus GetLolStatus(string userSid)
    {
        var now = _clock.UtcNow;
        var limit = _settings().LolPvpMatches;
        var counted = _store.GetMatchesSince(userSid, now - Window)
            .Where(m => m.RemakeStatus != RemakeStatus.Remake)
            .ToList();
        var used = counted.Count;
        var blocked = used >= limit;

        DateTimeOffset? next = counted.Count > 0 ? counted[0].StartedAt + Window : null;
        DateTimeOffset? availableAgain = null;
        if (blocked && counted.Count > 0)
        {
            // used - limit + 1 meccsnek kell kiesnie ahhoz, hogy újra legyen szabad alkalom.
            var index = used - limit;
            availableAgain = counted[Math.Min(index, counted.Count - 1)].StartedAt + Window;
        }

        return new LolLimitStatus(limit, used, Math.Max(0, limit - used), blocked, next, availableAgain);
    }
}
