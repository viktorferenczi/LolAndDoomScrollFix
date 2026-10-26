namespace Limiter.Core.Lol;

/// <summary>
/// A LoL-kliens lekérdezési üteme. Játék közben ritkább, hogy a szolgáltatás a lehető legkevesebb terhet
/// tegye a gépre (és a kliensre), amíg a meccs fut; a fázisváltásokra az események azonnal kiértékelést indítanak.
/// </summary>
public static class LcuPolling
{
    public static readonly TimeSpan Normal = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan InGame = TimeSpan.FromSeconds(15);

    public static TimeSpan IntervalFor(string? phase) =>
        phase is not null && LolMatchTracker.RunningPhases.Contains(phase) ? InGame : Normal;
}
