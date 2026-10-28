using System.Text.Json;
using Limiter.Core.Lol;

namespace Limiter.Service.Lcu;

/// <summary>
/// A manual check tool: <c>Limiter.Service.exe --diagnose [--test-cancel]</c>.
/// Prints the client's state, the queue's classification and the remake flag of the last matches; with --test-cancel
/// it cancels a search in progress / declines the match accept, so that the behaviour can be verified on the current client.
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
            Console.WriteLine($"ERROR: discovering the LoL client failed: {ex.Message}");
            return 2;
        }

        if (processes.Count == 0)
        {
            Console.WriteLine("No LoL client is running (LeagueClientUx.exe).");
            return 1;
        }

        foreach (var p in processes)
        {
            Console.WriteLine($"=== LoL client: PID {p.ProcessId}, port {p.Port}, user {p.OwnerSid}");
            using var api = new LcuApi(p.Port, p.Token);
            try
            {
                var local = await api.GetLocalPlayerAsync(ct);
                Console.WriteLine($"Signed-in player: puuid={local?.Puuid ?? "?"}");

                var phase = await api.GetPhaseAsync(ct);
                Console.WriteLine($"Phase: {phase}");

                var snapshot = await api.GetSnapshotAsync(local, ct);
                var queue = phase is "Matchmaking" or "ReadyCheck" or "Lobby" ? await api.ResolveQueueAsync(ct) : snapshot.Queue;
                Console.WriteLine($"Queue: id={queue.QueueId}, mode={queue.GameMode}, category={queue.Category}, type={queue.Type}, custom={queue.IsCustom}");
                Console.WriteLine($"Classification: {LolQueueClassifier.Classify(queue)}");
                if (snapshot.GameId > 0)
                    Console.WriteLine($"Match: {snapshot.GameId}, participant: {snapshot.LocalPlayerIsParticipant?.ToString() ?? "unknown"}");

                if (testCancel)
                {
                    if (phase == "Matchmaking")
                        Console.WriteLine($"Cancelling the search: HTTP {(int)await api.CancelSearchAsync(ct)}");
                    else if (phase == "ReadyCheck")
                        Console.WriteLine($"Declining the match accept: HTTP {(int)await api.DeclineReadyCheckAsync(ct)}");
                    else
                        Console.WriteLine("--test-cancel: start a search and run this again while searching or at the accept prompt.");
                }

                Console.WriteLine("Recent matches (remake = gameEndedInEarlySurrender):");
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
                    Console.WriteLine("  (match history is not available)");
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
