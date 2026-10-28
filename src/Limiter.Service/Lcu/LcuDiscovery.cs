using System.Management;
using Limiter.Core.Lol;

namespace Limiter.Service.Lcu;

/// <summary>A running LoL client: the process, the owner Windows user and access to the local API.</summary>
public sealed record LcuProcess(uint ProcessId, string OwnerSid, int Port, string Token);

public static class LcuDiscovery
{
    /// <summary>
    /// Discovering the running LeagueClientUx.exe processes through WMI. The port and the password are in the command line;
    /// the owner SID decides which Windows user's limit has to be used.
    /// </summary>
    public static IReadOnlyList<LcuProcess> Find()
    {
        var result = new List<LcuProcess>();
        // SELECT *: the result of a column-restricted query has no object path, and on it a
        // GetOwnerSid method call throws an InvalidOperationException.
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_Process WHERE Name = 'LeagueClientUx.exe'");
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
            catch (Exception ex) when (ex is ManagementException or InvalidOperationException or UnauthorizedAccessException)
            {
                // One faulty process must not block the discovery of the others.
            }

            if (!string.IsNullOrEmpty(sid)) result.Add(new LcuProcess(pid, sid, port, token));
        }
        return result;
    }
}
