using Limiter.Core;
using Limiter.Core.Lol;

namespace Limiter.Service.Lcu;

/// <summary>3 másodpercenként felderíti a futó LoL-klienseket, és mindegyikhez saját <see cref="LcuSession"/>-t indít.</summary>
public sealed class LcuSupervisor(
    LolMatchTracker tracker,
    LimitEngine engine,
    ProtectionState protection,
    IClock clock,
    ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger _log = loggers.CreateLogger<LcuSupervisor>();
    private readonly Dictionary<LcuProcess, (CancellationTokenSource Cts, Task Task, LcuSession Session)> _sessions = [];

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
            return;
        }

        foreach (var gone in _sessions.Keys.Except(found).ToList())
            _ = StopAsync(gone);

        foreach (var process in found.Where(p => !_sessions.ContainsKey(p)))
        {
            _log.LogInformation("LoL-kliens észlelve: PID {Pid}, felhasználó {Sid}", process.ProcessId, process.OwnerSid);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var session = new LcuSession(process, tracker, engine, protection, clock, loggers.CreateLogger<LcuSession>());
            _sessions[process] = (cts, Task.Run(() => session.RunAsync(cts.Token), cts.Token), session);
        }
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
