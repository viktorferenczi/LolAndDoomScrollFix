namespace Limiter.Core.Lol;

/// <summary>
/// The polling rhythm of the LoL client. Slower during a game so that the service puts the least possible load
/// on the machine (and the client) while the match runs; events trigger an evaluation immediately on phase changes.
/// </summary>
public static class LcuPolling
{
    public static readonly TimeSpan Normal = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan InGame = TimeSpan.FromSeconds(15);

    public static TimeSpan IntervalFor(string? phase) =>
        phase is not null && LolMatchTracker.RunningPhases.Contains(phase) ? InGame : Normal;
}
