using System.Diagnostics;
using Limiter.Protocol;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Limiter.Service;

/// <summary>
/// At every start the service closes its own permissions, so that administrators cannot stop, disable or delete it with
/// the usual tools either. Stopping it from the admin panel (with the password) opens them, and the next start closes them again.
/// </summary>
public static class ServiceLock
{
    public static void Apply()
    {
        if (!WindowsServiceHelpers.IsWindowsService()) return;
        try
        {
            using var sc = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"),
                ["sdset", IpcConstants.ServiceName, IpcConstants.ServiceSecurity.Locked]) { UseShellExecute = false, CreateNoWindow = true });
            sc?.WaitForExit(10_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The limits must work even if this fails; the installer has also closed the permissions.
        }
    }
}
