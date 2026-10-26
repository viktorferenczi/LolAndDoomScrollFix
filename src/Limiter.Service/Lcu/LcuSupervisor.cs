using System.Diagnostics;
using Limiter.Core;
using Limiter.Core.Lol;

namespace Limiter.Service.Lcu;

/// <summary>
/// 3 másodpercenként megnézi, fut-e LoL-kliens (olcsó folyamatlista), és csak új kliens esetén kérdezi le a részleteit
/// (WMI); mindegyikhez saját <see cref="LcuSession"/>-t indít.
/// </summary>
public sealed class LcuSupervisor(
    LolMatchTracker tracker,
    LimitEngine engine,
    ProtectionState protection,
    IClock clock,
    ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger _log = loggers.CreateLogger<LcuSupervisor>();
    private static readonly TimeSpan DiscoveryRetry = TimeSpan.FromSeconds(15);
    private readonly Dictionary<LcuProcess, (CancellationTokenSource Cts, Task Task, LcuSession Session)> _sessions = [];
    private DateTimeOffset _nextDiscoveryAllowed = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                Scan(ct);
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var key in _sessions.Keys.ToList()) await StopAsync(key).ConfigureAwait(false);
        }
    }

    private void Scan(CancellationToken ct)
    {
        // 1. Olcsó lépés: csak a futó kliensfolyamatok azonosítóit listázzuk (a folyamattábla pillanatképe).
        //    Itt semmilyen más folyamatot (a játékot sem) nem nyitunk meg, és semmi más tulajdonságot nem olvasunk.
        HashSet<uint> running;
        try
        {
            running = RunningClientPids();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A futó LoL-kliens listázása sikertelen.");
            protection.SetDiscoveryError("A LoL-kliens felderítése sikertelen — a meccskorlát most nem érvényesül.");
            return;
        }

        foreach (var gone in _sessions.Keys.Where(p => !running.Contains(p.ProcessId)).ToList())
            _ = StopAsync(gone);

        var known = _sessions.Keys.Select(p => p.ProcessId).ToHashSet();
        var unresolved = running.Where(pid => !known.Contains(pid)).ToList();
        if (unresolved.Count == 0)
        {
            // Nincs új kliens (vagy nem fut): a költséges WMI-lekérdezés ilyenkor nem indul el.
            if (running.Count == 0) protection.SetDiscoveryError(null);
            return;
        }

        if (clock.UtcNow < _nextDiscoveryAllowed) return;

        // 2. Költséges lépés (WMI): csak új kliensfolyamat esetén — port, jelszó és tulajdonos kiolvasása.
        IReadOnlyList<LcuProcess> found;
        try
        {
            found = LcuDiscovery.Find();
            protection.SetDiscoveryError(null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A LoL-kliens felderítése sikertelen.");
            protection.SetDiscoveryError("A LoL-kliens felderítése sikertelen — a meccskorlát most nem érvényesül.");
            _nextDiscoveryAllowed = clock.UtcNow + DiscoveryRetry;
            return;
        }

        foreach (var process in found.Where(p => unresolved.Contains(p.ProcessId) && !_sessions.ContainsKey(p)))
        {
            _log.LogInformation("LoL-kliens észlelve: PID {Pid}, felhasználó {Sid}", process.ProcessId, process.OwnerSid);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var session = new LcuSession(process, tracker, engine, protection, clock, loggers.CreateLogger<LcuSession>());
            _sessions[process] = (cts, Task.Run(() => session.RunAsync(cts.Token), cts.Token), session);
        }

        // Ha egy futó klienst nem sikerült azonosítani (még indul), nem kérdezünk újra azonnal.
        if (unresolved.Any(pid => found.All(f => f.ProcessId != pid)))
            _nextDiscoveryAllowed = clock.UtcNow + DiscoveryRetry;
    }

    private static HashSet<uint> RunningClientPids()
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("LeagueClientUx"))
        {
            using (p) pids.Add((uint)p.Id);
        }
        return pids;
    }

    private async Task StopAsync(LcuProcess process)
    {
        if (!_sessions.Remove(process, out var entry)) return;
        _log.LogInformation("LoL-kliens leállt: PID {Pid}", process.ProcessId);
        await entry.Cts.CancelAsync().ConfigureAwait(false);
        try { await entry.Task.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException) { }
        entry.Session.Dispose();
        entry.Cts.Dispose();
    }
}
