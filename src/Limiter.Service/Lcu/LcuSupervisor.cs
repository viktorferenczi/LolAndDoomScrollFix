using System.Diagnostics;
using Limiter.Core;
using Limiter.Core.Lol;

namespace Limiter.Service.Lcu;

/// <summary>
/// Every 3 seconds it checks whether a LoL client is running (a cheap process list), and only for a new client queries its details
/// (WMI); it starts a separate <see cref="LcuSession"/> for each.
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
        // 1. Cheap step: we only list the IDs of the running client processes (a snapshot of the process table).
        //    Here we open no other process (not the game either), and read no other property.
        HashSet<uint> running;
        try
        {
            running = RunningClientPids();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Listing the running LoL client failed.");
            protection.SetDiscoveryError(Loc.T("Discovering the LoL client failed — the match limit is not enforced right now.", "A LoL-kliens felderítése sikertelen — a meccskorlát most nem érvényesül."));
            return;
        }

        foreach (var gone in _sessions.Keys.Where(p => !running.Contains(p.ProcessId)).ToList())
            _ = StopAsync(gone);

        var known = _sessions.Keys.Select(p => p.ProcessId).ToHashSet();
        var unresolved = running.Where(pid => !known.Contains(pid)).ToList();
        if (unresolved.Count == 0)
        {
            // No new client (or none running): the expensive WMI query does not start then.
            if (running.Count == 0) protection.SetDiscoveryError(null);
            return;
        }

        if (clock.UtcNow < _nextDiscoveryAllowed) return;

        // 2. Expensive step (WMI): only for a new client process — reading the port, the password and the owner.
        IReadOnlyList<LcuProcess> found;
        try
        {
            found = LcuDiscovery.Find();
            protection.SetDiscoveryError(null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Discovering the LoL client failed.");
            protection.SetDiscoveryError(Loc.T("Discovering the LoL client failed — the match limit is not enforced right now.", "A LoL-kliens felderítése sikertelen — a meccskorlát most nem érvényesül."));
            _nextDiscoveryAllowed = clock.UtcNow + DiscoveryRetry;
            return;
        }

        foreach (var process in found.Where(p => unresolved.Contains(p.ProcessId) && !_sessions.ContainsKey(p)))
        {
            _log.LogInformation("LoL client detected: PID {Pid}, user {Sid}", process.ProcessId, process.OwnerSid);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var session = new LcuSession(process, tracker, engine, protection, clock, loggers.CreateLogger<LcuSession>());
            _sessions[process] = (cts, Task.Run(() => session.RunAsync(cts.Token), cts.Token), session);
        }

        // If a running client could not be identified (still starting), we do not query again immediately.
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
        _log.LogInformation("LoL client stopped: PID {Pid}", process.ProcessId);
        await entry.Cts.CancelAsync().ConfigureAwait(false);
        try { await entry.Task.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException) { }
        entry.Session.Dispose();
        entry.Cts.Dispose();
    }
}
