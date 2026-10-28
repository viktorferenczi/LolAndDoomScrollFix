using Limiter.Core;

namespace Limiter.Service;

/// <summary>Re-reading the limits (30 s) and deleting old records that no longer count (hourly).</summary>
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
                    // For the rest-day bonus, a day beyond a weekend must also be looked back at.
                    store.Prune(clock.UtcNow - TimeSpan.FromDays(9));
                    lastPrune = clock.UtcNow;
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Deleting old records failed.");
                }
            }
        } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }
}
