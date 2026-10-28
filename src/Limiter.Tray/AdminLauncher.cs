using Limiter.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Limiter.Tray;

/// <summary>
/// Starting and protecting the admin panel. The program restarts itself with administrator rights
/// (<c>--admin</c>): Windows then asks for the administrator password (UAC), and the window only appears after approval.
/// Then the admin password has to be typed too (<see cref="AdminGate"/>); the user cannot reach limits.json anyway.
/// </summary>
public static class AdminLauncher
{
    public const string Argument = "--admin";
    private const int ErrorCancelledByUser = 1223;
    private static string Title => Loc.T("Admin settings", "Admin beállítások");

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LolScrollLimiter", "limits.json");

    /// <summary>Restarts the program with elevated rights. Without approval nothing opens.</summary>
    public static void Open(IWin32Window? owner)
    {
        try
        {
            // UseShellExecute + runas: only this way does the administrator password prompt appear.
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Argument) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledByUser)
        {
            MessageBox.Show(owner,
                Loc.T("The admin panel did not open: the administrator approval (password) was not given.\nAsk the person who knows the administrator password.",
                      "Az admin felület nem nyílt meg: a rendszergazdai jóváhagyás (jelszó) elmaradt.\nKérd meg a rendszergazdai jelszót ismerő személyt."),
                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(owner, Loc.T("The admin panel cannot be started: ", "Az admin felület nem indítható: ") + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Whether the process runs with elevated (administrator) rights.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>The entry point of the elevated process (<c>Limiter.Tray.exe --admin</c>).</summary>
    public static void RunElevated()
    {
        if (!IsElevated())
        {
            // Started without approval (e.g. by hand, from the command line) it does not open either, and could not write anyway.
            MessageBox.Show(Loc.T("The admin panel can only be opened with administrator rights.\nUse the \"Admin settings\" button of the tray application.",
                                  "Az admin felület csak rendszergazdai jogosultsággal nyitható meg.\nHasználd a tálcaalkalmazás „Admin beállítások” gombját."),
                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, @"Local\LolScrollLimiterAdmin", out var created);
        if (!created) return;

        // Second gate: the admin password (in addition to the Windows administrator approval).
        if (!AdminGate.Unlock(null)) return;

        Application.Run(new AdminForm(SettingsPath));
    }
}
