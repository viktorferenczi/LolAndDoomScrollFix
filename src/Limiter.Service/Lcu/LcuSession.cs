using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Limiter.Core;
using Limiter.Core.Lol;
using Limiter.Protocol;

namespace Limiter.Service.Lcu;

/// <summary>
/// Kapcsolat egy futó LoL-klienssel. Eseményekre (WebSocket) és 2 másodperces lekérdezésre is kiértékel,
/// így egy elmaradt esemény sem enged át keresést. Hiba esetén enged (fail-open) és jelzi a hibát.
/// </summary>
public sealed class LcuSession : IDisposable
{
    private static readonly string[] Events =
    [
        "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",
        "OnJsonApiEvent_lol-gameflow_v1_session",
        "OnJsonApiEvent_lol-matchmaking_v1_search",
        "OnJsonApiEvent_lol-matchmaking_v1_ready-check",
        "OnJsonApiEvent_lol-lobby_v2_lobby",
    ];

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RemakeInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RemakeGiveUpAfter = TimeSpan.FromHours(6);
    private static readonly TimeSpan ErrorGrace = TimeSpan.FromSeconds(30);

    private readonly LcuProcess _process;
    private readonly LcuApi _api;
    private readonly LolMatchTracker _tracker;
    private readonly LimitEngine _engine;
    private readonly ProtectionState _protection;
    private readonly IClock _clock;
    private readonly ILogger _log;
    private readonly Channel<bool> _evaluateKick = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Channel<bool> _remakeKick = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private LocalPlayer? _localPlayer;
    private long _runningGameId;
    private DateTimeOffset _lastSuccess;
    private string? _degradedReason;

    public LcuSession(LcuProcess process, LolMatchTracker tracker, LimitEngine engine, ProtectionState protection, IClock clock, ILogger log)
    {
        _process = process;
        _api = new LcuApi(process.Port, process.Token);
        _tracker = tracker;
        _engine = engine;
        _protection = protection;
        _clock = clock;
        _log = log;
        _lastSuccess = clock.UtcNow;
    }

    private string Sid => _process.OwnerSid;

    public async Task RunAsync(CancellationToken ct)
    {
        _protection.SetLcu(Sid, IpcConstants.LcuStates.Connecting, "Kapcsolódás a LoL-klienshez…");
        try
        {
            await Task.WhenAll(EventLoopAsync(ct), EvaluateLoopAsync(ct), RemakeLoopAsync(ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            _protection.SetLcu(Sid, IpcConstants.LcuStates.NotRunning);
        }
    }

    private async Task EventLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = await _api.ConnectEventsAsync(Events, ct).ConfigureAwait(false);
                _evaluateKick.Writer.TryWrite(true);
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    // Az esemény tartalma nem érdekes: minden releváns változás teljes kiértékelést indít.
                    if (result.EndOfMessage) _evaluateKick.Writer.TryWrite(true);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException)
            {
                _log.LogDebug(ex, "LCU WebSocket hiba, újrapróbálás.");
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        }
    }

    private async Task EvaluateLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(PollInterval);
                try { await _evaluateKick.Reader.ReadAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            }

            try
            {
                await EvaluateAsync(ct).ConfigureAwait(false);
                _lastSuccess = _clock.UtcNow;
                if (_degradedReason is { } reason)
                    _protection.SetLcu(Sid, IpcConstants.LcuStates.Degraded, reason);
                else
                    _protection.SetLcu(Sid, IpcConstants.LcuStates.Connected);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "LCU kiértékelési hiba.");
                if (_clock.UtcNow - _lastSuccess > ErrorGrace)
                {
                    _protection.SetLcu(Sid, IpcConstants.LcuStates.Error,
                        "A LoL-kliens nem válaszol — a meccskorlát most nem érvényesül.");
                }
            }
        }
    }

    private async Task EvaluateAsync(CancellationToken ct)
    {
        _degradedReason = null;
        var phase = await _api.GetPhaseAsync(ct).ConfigureAwait(false);

        if (phase is "Matchmaking" or "ReadyCheck")
        {
            _tracker.NotePreGame(Sid);
            var queue = await _api.ResolveQueueAsync(ct).ConfigureAwait(false);
            var decision = _tracker.DecideMatchmaking(Sid, phase, queue);
            if (decision.Kind == GameKind.Unknown)
                _degradedReason = "Nem azonosítható játékmód — a program enged, de jelzi a hibát.";
            await ApplyDecisionAsync(decision.Action, ct).ConfigureAwait(false);
            return;
        }

        if (phase == "ChampSelect")
        {
            _tracker.NotePreGame(Sid);
            return;
        }

        if (LolMatchTracker.RunningPhases.Contains(phase))
        {
            _localPlayer ??= await _api.GetLocalPlayerAsync(ct).ConfigureAwait(false);
            var snapshot = await _api.GetSnapshotAsync(_localPlayer, ct).ConfigureAwait(false);
            _runningGameId = snapshot.GameId;
            var outcome = _tracker.OnGameflow(Sid, snapshot);
            switch (outcome)
            {
                case MatchRecordOutcome.Recorded:
                    var status = _engine.GetLolStatus(Sid);
                    _log.LogInformation("PvP-meccs rögzítve: {GameId} (sor {Queue}), {Used}/{Limit}",
                        snapshot.GameId, snapshot.Queue.QueueId, status.Used, status.Limit);
                    break;
                case MatchRecordOutcome.Unknown:
                    _degradedReason = "A futó meccs nem azonosítható — nem került rögzítésre.";
                    break;
            }
            return;
        }

        if (LolMatchTracker.PostGamePhases.Contains(phase))
            _remakeKick.Writer.TryWrite(true);
        _runningGameId = 0;
    }

    private async Task ApplyDecisionAsync(MatchmakingAction action, CancellationToken ct)
    {
        switch (action)
        {
            case MatchmakingAction.CancelSearch:
            {
                var status = await _api.CancelSearchAsync(ct).ConfigureAwait(false);
                Report(status, "Új PvP-keresés megszakítva: elfogyott a 24 órás meccskeret.",
                    "A PvP-keresést nem sikerült megszakítani; a meccselfogadást a program el fogja utasítani.");
                break;
            }
            case MatchmakingAction.DeclineReadyCheck:
            {
                var status = await _api.DeclineReadyCheckAsync(ct).ConfigureAwait(false);
                await _api.CancelSearchAsync(ct).ConfigureAwait(false);
                Report(status, "PvP-meccselfogadás elutasítva: elfogyott a 24 órás meccskeret.",
                    "A meccselfogadást nem sikerült elutasítani.");
                break;
            }
        }

        void Report(HttpStatusCode status, string success, string failure)
        {
            var ok = (int)status is >= 200 and < 300;
            _protection.LolAction(Sid, ok ? success : $"{failure} (HTTP {(int)status})");
            if (ok) _log.LogInformation("{Message}", success);
            else
            {
                _degradedReason = failure;
                _log.LogWarning("{Message} HTTP {Status}", failure, (int)status);
            }
        }
    }

    private async Task RemakeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(RemakeInterval);
                try
                {
                    await _remakeKick.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                    // A meccs végi statisztika pár másodperc múlva áll elő.
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            }

            foreach (var match in _tracker.PendingRemakeChecks(Sid))
            {
                if (match.GameId == _runningGameId) continue;
                try
                {
                    var remade = await _api.CheckRemakeAsync(match.GameId, ct).ConfigureAwait(false);
                    if (remade is { } r)
                    {
                        _tracker.OnRemakeResult(Sid, match.GameId, r);
                        if (r)
                        {
                            _protection.LolAction(Sid, "Igazolt remake: a meccs nem számít bele a keretbe.");
                            _log.LogInformation("Remake igazolva: {GameId}", match.GameId);
                        }
                    }
                    else if (_clock.UtcNow - match.StartedAt > RemakeGiveUpAfter)
                    {
                        // Nem igazolható remake: a meccs számít (rövid játékidő önmagában nem elég).
                        _tracker.OnRemakeResult(Sid, match.GameId, remade: false);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Remake-ellenőrzés sikertelen: {GameId}", match.GameId);
                }
            }
        }
    }

    public void Dispose() => _api.Dispose();
}
