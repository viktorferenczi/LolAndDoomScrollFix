using System.Text.Json;
using System.Text.RegularExpressions;

namespace Limiter.Core.Lol;

/// <summary>A helyi kliensből (/lol-gameflow/v1/session) kiolvasott pillanatkép.</summary>
public sealed record GameflowSnapshot(string Phase, long GameId, QueueInfo Queue, bool? LocalPlayerIsParticipant);

public sealed record LocalPlayer(string? Puuid, long? SummonerId);

/// <summary>A League Client (LCU) JSON-válaszainak tűrő feldolgozása. Hiányzó mezőnél „ismeretlen” értéket ad, nem kivételt.</summary>
public static partial class LcuJson
{
    public static string ParsePhase(JsonElement phaseJson) =>
        phaseJson.ValueKind == JsonValueKind.String ? phaseJson.GetString() ?? "None" : "None";

    public static LocalPlayer ParseLocalPlayer(JsonElement summoner) =>
        new(GetString(summoner, "puuid"), GetInt64(summoner, "summonerId"));

    public static QueueInfo ParseQueue(JsonElement queue, bool isCustom = false)
    {
        if (queue.ValueKind != JsonValueKind.Object) return QueueInfo.Empty with { IsCustom = isCustom };
        return new QueueInfo(
            (int)(GetInt64(queue, "id") ?? 0),
            GetString(queue, "gameMode"),
            GetString(queue, "category"),
            GetString(queue, "type"),
            isCustom || GetBool(queue, "isCustom") == true);
    }

    public static GameflowSnapshot ParseSession(JsonElement session, LocalPlayer? local)
    {
        var phase = GetString(session, "phase") ?? "None";
        if (!session.TryGetProperty("gameData", out var gameData) || gameData.ValueKind != JsonValueKind.Object)
            return new GameflowSnapshot(phase, 0, QueueInfo.Empty, null);

        var gameId = GetInt64(gameData, "gameId") ?? 0;
        var isCustom = GetBool(gameData, "isCustomGame") == true;
        var queue = gameData.TryGetProperty("queue", out var q) ? ParseQueue(q, isCustom) : QueueInfo.Empty with { IsCustom = isCustom };
        return new GameflowSnapshot(phase, gameId, queue, IsParticipant(gameData, local));
    }

    /// <summary>A lobby (/lol-lobby/v2/lobby) sorbeállítása.</summary>
    public static QueueInfo ParseLobbyQueue(JsonElement lobby)
    {
        if (!lobby.TryGetProperty("gameConfig", out var config) || config.ValueKind != JsonValueKind.Object)
            return QueueInfo.Empty;
        return new QueueInfo(
            (int)(GetInt64(config, "queueId") ?? 0),
            GetString(config, "gameMode"),
            null,
            null,
            GetBool(config, "isCustom") == true);
    }

    public static long? ParseGameId(JsonElement json) => GetInt64(json, "gameId");

    /// <summary>
    /// Igazolt remake keresése: a meccs adataiban bárhol szereplő <c>gameEndedInEarlySurrender</c> mező.
    /// Pusztán a rövid játékidő nem számít remake-nek.
    /// </summary>
    /// <returns>true: remake; false: a mező létezik, de egyik sem igaz; null: nincs ilyen mező.</returns>
    public static bool? ParseEarlySurrender(JsonElement game)
    {
        bool found = false, any = false;
        Walk(game);
        return found ? any : null;

        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        if (p.NameEquals("gameEndedInEarlySurrender") &&
                            p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            found = true;
                            any |= p.Value.GetBoolean();
                        }
                        else
                        {
                            Walk(p.Value);
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) Walk(item);
                    break;
            }
        }
    }

    /// <summary>Port és jelszó kiolvasása a LeagueClientUx.exe parancssorából.</summary>
    public static bool TryParseCommandLine(string? commandLine, out int port, out string token)
    {
        port = 0;
        token = "";
        if (string.IsNullOrEmpty(commandLine)) return false;
        var portMatch = PortRegex().Match(commandLine);
        var tokenMatch = TokenRegex().Match(commandLine);
        if (!portMatch.Success || !tokenMatch.Success) return false;
        if (!int.TryParse(portMatch.Groups[1].Value, out port) || port is <= 0 or > 65535) return false;
        token = tokenMatch.Groups[1].Value;
        return token.Length > 0;
    }

    private static bool? IsParticipant(JsonElement gameData, LocalPlayer? local)
    {
        if (local is null || (local.Puuid is null && local.SummonerId is null)) return null;
        var sawAnyPlayer = false;
        foreach (var teamName in new[] { "teamOne", "teamTwo" })
        {
            if (!gameData.TryGetProperty(teamName, out var team) || team.ValueKind != JsonValueKind.Array) continue;
            foreach (var player in team.EnumerateArray())
            {
                if (player.ValueKind != JsonValueKind.Object) continue;
                sawAnyPlayer = true;
                var puuid = GetString(player, "puuid");
                var summonerId = GetInt64(player, "summonerId");
                if (local.Puuid is not null && puuid == local.Puuid) return true;
                if (local.SummonerId is > 0 && summonerId == local.SummonerId) return true;
            }
        }
        return sawAnyPlayer ? false : null;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? GetInt64(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out n)) return n;
        return null;
    }

    private static bool? GetBool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    [GeneratedRegex(@"--app-port=(\d+)")]
    private static partial Regex PortRegex();

    [GeneratedRegex(@"--remoting-auth-token=([A-Za-z0-9_\-]+)")]
    private static partial Regex TokenRegex();
}
