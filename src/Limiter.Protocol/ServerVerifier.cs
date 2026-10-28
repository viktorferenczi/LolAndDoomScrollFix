using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Limiter.Core;

namespace Limiter.Protocol;

/// <summary>
/// Ellenőrzi, hogy a pipe túloldalán a valódi szolgáltatás fut-e:
/// a pipe-szerver a 0-s (szolgáltatás-) munkamenetben van, és a folyamatazonosítója megegyezik azzal,
/// amit a Szolgáltatásvezérlő (SCM) a LolScrollLimiter szolgáltatásról jelent.
/// Egyik lekérdezés sem igényel hozzáférést a szolgáltatás folyamatához (azt normál felhasználó nem kap meg);
/// a szolgáltatás állapotát a szolgáltatás ACL-je szerint bárki lekérdezheti, a bináris útvonalát viszont csak
/// rendszergazda módosíthatja, így ugyanolyan erős, mint az elérési út ellenőrzése.
/// </summary>
public static class ServerVerifier
{
    public static void EnsureTrustedServer(NamedPipeClientStream pipe)
    {
        if (!OperatingSystem.IsWindows()) return;
#if DEBUG
        // Fejlesztői build: a szolgáltatás konzolból is futhat.
        return;
#else
        if (!GetNamedPipeServerSessionId(pipe.SafePipeHandle, out var sessionId))
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("The pipe server's session cannot be queried.", "A pipe-szerver munkamenete nem kérdezhető le."));
        if (sessionId != 0)
            throw new UnauthorizedAccessException(Loc.T("The pipe is not owned by the limiter service (it does not run in session 0).", "A pipe-ot nem a korlátozó szolgáltatás birtokolja (nem a 0-s munkamenetben fut)."));

        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pipePid))
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("The pipe server's process cannot be identified.", "A pipe-szerver folyamata nem azonosítható."));

        var servicePid = QueryServiceProcessId(IpcConstants.ServiceName);
        if (servicePid == 0)
            throw new UnauthorizedAccessException(Loc.T("The limiter service is not running.", "A korlátozó szolgáltatás nem fut."));
        if (servicePid != pipePid)
            throw new UnauthorizedAccessException(
                Loc.T($"The pipe is not owned by the limiter service (pipe: {pipePid}, service: {servicePid}).", $"A pipe-ot nem a korlátozó szolgáltatás birtokolja (pipe: {pipePid}, szolgáltatás: {servicePid})."));
#endif
    }

    /// <summary>A szolgáltatás folyamatazonosítója az SCM szerint (0, ha nem fut).</summary>
    private static uint QueryServiceProcessId(string serviceName)
    {
        const uint ScManagerConnect = 0x0001;
        const uint ServiceQueryStatus = 0x0004;
        const int ScStatusProcessInfo = 0;

        using var scm = OpenSCManager(null, null, ScManagerConnect);
        if (scm.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("The Service Control Manager is not reachable.", "A Szolgáltatásvezérlő nem érhető el."));

        using var service = OpenService(scm, serviceName, ServiceQueryStatus);
        if (service.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("The limiter service was not found.", "A korlátozó szolgáltatás nem található."));

        if (!QueryServiceStatusEx(service, ScStatusProcessInfo, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("The limiter service's state cannot be queried.", "A korlátozó szolgáltatás állapota nem kérdezhető le."));

        return status.ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    private sealed class SafeServiceHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint serverSessionId);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")]
    private static extern SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(SafeServiceHandle service, int infoLevel,
        out ServiceStatusProcess buffer, int bufferSize, out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
