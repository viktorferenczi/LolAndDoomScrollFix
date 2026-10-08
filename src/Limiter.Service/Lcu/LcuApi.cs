using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Limiter.Core.Lol;

namespace Limiter.Service.Lcu;

public sealed class LcuHttpException(HttpStatusCode status, string path)
    : Exception($"A LoL-kliens {(int)status} választ adott: {path}")
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>
/// Vékony réteg a League Client helyi REST API-ja fölött (https://127.0.0.1:port, „riot” felhasználó).
/// A Riot ezt a felületet harmadik félnek hivatalosan nem támogatja, ezért minden hívás hibatűrő.
/// </summary>
public sealed class LcuApi : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _authHeader;

    public LcuApi(int port, string token)
    {
        Port = port;
        _authHeader = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + token));
        var handler = new SocketsHttpHandler
        {
            // A kliens saját (Riot) tanúsítványt használ; csak a 127.0.0.1-re szóló kapcsolatnál fogadjuk el.
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(5),
        };
        _http.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(_authHeader);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public int Port { get; }

    /// <summary>GET; 404 esetén null.</summary>
    public async Task<JsonElement?> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path.TrimStart('/'), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new LcuHttpException(response.StatusCode, path);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return doc.RootElement.Clone();
    }

    public async Task<HttpStatusCode> SendAsync(HttpMethod method, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return response.StatusCode;
    }

    public async Task<string> GetPhaseAsync(CancellationToken ct) =>
        await GetAsync("/lol-gameflow/v1/gameflow-phase", ct).ConfigureAwait(false) is { } phase
            ? LcuJson.ParsePhase(phase)
            : "None";

    public async Task<LocalPlayer?> GetLocalPlayerAsync(CancellationToken ct) =>
        await GetAsync("/lol-summoner/v1/current-summoner", ct).ConfigureAwait(false) is { } s
            ? LcuJson.ParseLocalPlayer(s)
            : null;

    public async Task<GameflowSnapshot> GetSnapshotAsync(LocalPlayer? local, CancellationToken ct) =>
        await GetAsync("/lol-gameflow/v1/session", ct).ConfigureAwait(false) is { } session
            ? LcuJson.ParseSession(session, local)
            : new GameflowSnapshot("None", 0, QueueInfo.Empty, null);

    /// <summary>
    /// A keresés/elfogadás alatti sor meghatározása: elsőként a gameflow-munkamenetből, majd a lobbyból,
    /// végül a sor részletes adataiból (kategória, típus).
    /// </summary>
    public async Task<QueueInfo> ResolveQueueAsync(CancellationToken ct)
    {
        var queue = (await GetSnapshotAsync(null, ct).ConfigureAwait(false)).Queue;
        if (queue.IsComplete) return queue;

        if (await GetAsync("/lol-lobby/v2/lobby", ct).ConfigureAwait(false) is { } lobby)
            queue = queue.FillFrom(LcuJson.ParseLobbyQueue(lobby));

        if (queue.QueueId > 0 && await GetAsync($"/lol-game-queues/v1/queues/{queue.QueueId}", ct).ConfigureAwait(false) is { } details)
            queue = queue.FillFrom(LcuJson.ParseQueue(details));

        return queue;
    }

    /// <summary>A PvP-keresés megszakítása.</summary>
    public Task<HttpStatusCode> CancelSearchAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, "/lol-lobby/v2/lobby/matchmaking/search", ct);

    /// <summary>A felugró meccselfogadás elutasítása.</summary>
    public Task<HttpStatusCode> DeclineReadyCheckAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/lol-matchmaking/v1/ready-check/decline", ct);

    /// <summary>
    /// Igazolt remake ellenőrzése: először a meccs végi statisztika (ha ugyanarról a meccsről szól, és remake-et mutat),
    /// majd a meccstörténet. null: még nem dönthető el.
    /// </summary>
    public async Task<bool?> CheckRemakeAsync(long gameId, CancellationToken ct)
    {
        try
        {
            if (await GetAsync("/lol-end-of-game/v1/eog-stats-block", ct).ConfigureAwait(false) is { } eog &&
                LcuJson.ParseGameId(eog) == gameId &&
                LcuJson.ParseEarlySurrender(eog) == true)
                return true;
        }
        catch (LcuHttpException) { }

        return await GetAsync($"/lol-match-history/v1/games/{gameId}", ct).ConfigureAwait(false) is { } game
            ? LcuJson.ParseEarlySurrender(game)
            : null;
    }

    /// <summary>WebSocket a kliens eseményeihez (WAMP 1.0: [5, "esemény"] feliratkozás).</summary>
    public async Task<ClientWebSocket> ConnectEventsAsync(IEnumerable<string> events, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", _authHeader);
        ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        ws.Options.AddSubProtocol("wamp");
        try
        {
            await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{Port}/"), ct).ConfigureAwait(false);
            foreach (var e in events)
            {
                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[] { 5, e }));
                await ws.SendAsync(payload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            return ws;
        }
        catch
        {
            ws.Dispose();
            throw;
        }
    }

    public void Dispose() => _http.Dispose();
}
