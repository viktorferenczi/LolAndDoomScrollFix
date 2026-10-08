using System.Management;
using Limiter.Core.Lol;

namespace Limiter.Service.Lcu;

/// <summary>Egy futó LoL-kliens: a folyamat, a tulajdonos Windows-felhasználó és a helyi API elérése.</summary>
public sealed record LcuProcess(uint ProcessId, string OwnerSid, int Port, string Token);

public static class LcuDiscovery
{
    /// <summary>
    /// A futó LeagueClientUx.exe folyamatok felderítése WMI-n keresztül. A port és a jelszó a parancssorban van;
    /// a tulajdonos SID határozza meg, melyik Windows-felhasználó keretét kell használni.
    /// </summary>
    public static IReadOnlyList<LcuProcess> Find()
    {
        var result = new List<LcuProcess>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'LeagueClientUx.exe'");
        using var processes = searcher.Get();
        foreach (var obj in processes)
        {
            using var mo = (ManagementObject)obj;
            var pid = (uint)mo["ProcessId"];
            if (!LcuJson.TryParseCommandLine(mo["CommandLine"] as string, out var port, out var token)) continue;

            string? sid = null;
            try
            {
                using var outParams = mo.InvokeMethod("GetOwnerSid", null, null);
                sid = outParams?["Sid"] as string;
            }
            catch (ManagementException) { }

            if (!string.IsNullOrEmpty(sid)) result.Add(new LcuProcess(pid, sid, port, token));
        }
        return result;
    }
}
