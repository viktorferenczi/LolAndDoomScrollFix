using System.Text.Json;
using Limiter.Core.Lol;

namespace Limiter.Service.Lcu;

/// <summary>
/// Kézi ellenőrző eszköz: <c>Limiter.Service.exe --diagnose [--test-cancel]</c>.
/// Kiírja a kliens állapotát, a sor besorolását és az utolsó meccsek remake-jelzőjét; --test-cancel esetén
/// a folyamatban lévő keresést megszakítja / a meccselfogadást elutasítja, hogy a működés a jelenlegi kliensen igazolható legyen.
/// </summary>
public static class LcuDiagnostics
{
    public static async Task<int> RunAsync(bool testCancel)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;

        IReadOnlyList<LcuProcess> processes;
        try { processes = LcuDiscovery.Find(); }
        catch (Exception ex)
        {
            Console.WriteLine($"HIBA: a LoL-kliens felderítése sikertelen: {ex.Message}");
            return 2;
        }

        if (processes.Count == 0)
        {
            Console.WriteLine("Nem fut LoL-kliens (LeagueClientUx.exe).");
            return 1;
        }

        foreach (var p in processes)
        {
            Console.WriteLine($"=== LoL-kliens: PID {p.ProcessId}, port {p.Port}, felhasználó {p.OwnerSid}");
            using var api = new LcuApi(p.Port, p.Token);
            try
            {
                var local = await api.GetLocalPlayerAsync(ct);
                Console.WriteLine($"Bejelentkezett játékos: puuid={local?.Puuid ?? "?"}");

                var phase = await api.GetPhaseAsync(ct);
                Console.WriteLine($"Fázis: {phase}");

                var snapshot = await api.GetSnapshotAsync(local, ct);
                var queue = phase is "Matchmaking" or "ReadyCheck" or "Lobby" ? await api.ResolveQueueAsync(ct) : snapshot.Queue;
                Console.WriteLine($"Sor: id={queue.QueueId}, mód={queue.GameMode}, kategória={queue.Category}, típus={queue.Type}, egyéni={queue.IsCustom}");
                Console.WriteLine($"Besorolás: {LolQueueClassifier.Classify(queue)}");
                if (snapshot.GameId > 0)
                    Console.WriteLine($"Meccs: {snapshot.GameId}, résztvevő: {snapshot.LocalPlayerIsParticipant?.ToString() ?? "ismeretlen"}");

                if (testCancel)
                {
                    if (phase == "Matchmaking")
                        Console.WriteLine($"Keresés megszakítása: HTTP {(int)await api.CancelSearchAsync(ct)}");
                    else if (phase == "ReadyCheck")
                        Console.WriteLine($"Meccselfogadás elutasítása: HTTP {(int)await api.DeclineReadyCheckAsync(ct)}");
                    else
                        Console.WriteLine("--test-cancel: indíts keresést, és futtasd újra keresés vagy elfogadás közben.");
                }

                Console.WriteLine("Utolsó meccsek (remake = gameEndedInEarlySurrender):");
                if (await api.GetAsync("/lol-match-history/v1/products/lol/current-summoner/matches?begIndex=0&endIndex=10", ct) is { } history &&
                    history.TryGetProperty("games", out var g1) && g1.TryGetProperty("games", out var games) && games.ValueKind == JsonValueKind.Array)
                {
                    foreach (var game in games.EnumerateArray())
                    {
                        var id = LcuJson.ParseGameId(game);
                        var duration = game.TryGetProperty("gameDuration", out var d) ? d.ToString() : "?";
                        var queueId = game.TryGetProperty("queueId", out var q) ? q.ToString() : "?";
                        var remake = LcuJson.ParseEarlySurrender(game);
                        Console.WriteLine($"  {id}  sor {queueId}  {duration} mp  remake: {remake?.ToString() ?? "nincs adat"}");
                    }
                }
                else
                {
                    Console.WriteLine("  (a meccstörténet nem érhető el)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HIBA: {ex.Message}");
                return 3;
            }
        }
        return 0;
    }
}
