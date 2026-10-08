using Limiter.Core;

namespace Limiter.Service;

/// <summary>Korlátok újraolvasása (30 mp) és a régi, már nem számító rekordok törlése (óránként).</summary>
public sealed class MaintenanceService(SettingsProvider settings, UsageStore store, IClock clock, ILogger<MaintenanceService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var lastPrune = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            settings.Reload();
            if (clock.UtcNow - lastPrune > TimeSpan.FromHours(1))
            {
                try
                {
                    store.Prune(clock.UtcNow - TimeSpan.FromDays(3));
                    lastPrune = clock.UtcNow;
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Régi rekordok törlése sikertelen.");
                }
            }
        } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }
}
