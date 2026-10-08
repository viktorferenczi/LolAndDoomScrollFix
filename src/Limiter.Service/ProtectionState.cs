using System.Collections.Concurrent;
using Limiter.Core;
using Limiter.Protocol;

namespace Limiter.Service;

/// <summary>A védelem működési állapota felhasználónként (LoL-kapcsolat, bővítmény jelentkezése, utolsó beavatkozás).</summary>
public sealed class ProtectionState(IClock clock)
{
    private sealed class UserState
    {
        public string LcuState = IpcConstants.LcuStates.NotRunning;
        public string? LcuMessage;
        public DateTimeOffset? LastExtensionPingAt;
        public string? LastLolAction;
        public DateTimeOffset? LastLolActionAt;
    }

    private readonly ConcurrentDictionary<string, UserState> _users = new(StringComparer.OrdinalIgnoreCase);
    private volatile string? _discoveryError;

    private UserState For(string sid) => _users.GetOrAdd(sid, _ => new UserState());

    public void SetLcu(string sid, string state, string? message = null)
    {
        var u = For(sid);
        lock (u)
        {
            u.LcuState = state;
            u.LcuMessage = message;
        }
    }

    public void ExtensionPing(string sid)
    {
        var u = For(sid);
        lock (u) u.LastExtensionPingAt = clock.UtcNow;
    }

    /// <summary>Egy LoL-beavatkozás (keresés megszakítása, elutasítás, remake) rögzítése. Ugyanazt az üzenetet 30 mp-en belül nem ismétli.</summary>
    public void LolAction(string sid, string message)
    {
        var u = For(sid);
        lock (u)
        {
            var now = clock.UtcNow;
            if (u.LastLolAction == message && u.LastLolActionAt is { } at && now - at < TimeSpan.FromSeconds(30)) return;
            u.LastLolAction = message;
            u.LastLolActionAt = now;
        }
    }

    public void SetDiscoveryError(string? message) => _discoveryError = message;

    public ProtectionDto Snapshot(string sid, IEnumerable<string> extraWarnings)
    {
        var u = For(sid);
        var dto = new ProtectionDto();
        lock (u)
        {
            dto.LcuState = u.LcuState;
            dto.LcuMessage = u.LcuMessage;
            dto.LastExtensionPingAt = u.LastExtensionPingAt;
            dto.LastLolAction = u.LastLolAction;
            dto.LastLolActionAt = u.LastLolActionAt;
        }
        if (_discoveryError is { } err) dto.Warnings.Add(err);
        dto.Warnings.AddRange(extraWarnings);
        return dto;
    }
}
