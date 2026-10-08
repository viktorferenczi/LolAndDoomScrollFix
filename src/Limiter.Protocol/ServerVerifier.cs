using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Limiter.Protocol;

/// <summary>
/// Ellenőrzi, hogy a pipe túloldalán a valódi szolgáltatás fut-e:
/// a folyamat a 0-s (szolgáltatás-) munkamenetben fut, és ugyanabból a mappából indult, mint a kliens
/// (a Program Files alatti telepítési mappa, amelyet normál felhasználó nem írhat).
/// </summary>
public static class ServerVerifier
{
    public static void EnsureTrustedServer(NamedPipeClientStream pipe)
    {
        if (!OperatingSystem.IsWindows()) return;
#if DEBUG
        // Fejlesztői build: a szolgáltatás konzolból, más mappából is futhat.
        return;
#else
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "A szolgáltatás folyamata nem azonosítható.");

        if (!ProcessIdToSessionId(pid, out var sessionId))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "A szolgáltatás munkamenete nem kérdezhető le.");
        if (sessionId != 0)
            throw new UnauthorizedAccessException("A pipe-ot nem a korlátozó szolgáltatás birtokolja (nem a 0-s munkamenetben fut).");

        var image = QueryImagePath(pid);
        var serverDir = Path.GetFullPath(Path.GetDirectoryName(image) ?? "");
        var ownDir = Path.GetFullPath(AppContext.BaseDirectory);
        if (!string.Equals(serverDir.TrimEnd('\\'), ownDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"A pipe-ot ismeretlen program birtokolja: {image}");
#endif
    }

    private static string QueryImagePath(uint pid)
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "A szolgáltatás folyamata nem nyitható meg.");
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "A szolgáltatás elérési útja nem kérdezhető le.");
        return buffer.ToString(0, size);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder exeName, ref int size);
}
