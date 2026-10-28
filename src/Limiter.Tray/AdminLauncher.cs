using Limiter.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Limiter.Tray;

/// <summary>
/// Starting and protecting the admin panel. The program restarts itself with administrator rights
/// (<c>--admin</c>): Windows then asks for the administrator password (UAC), and the window only appears after approval. Ezután az admin jelszót is be kell írni (<see cref="AdminGate"/>); a felhasználó a limits.json-hoz
/// amúgy sem fér hozzá.
/// </summary>
public static class AdminLauncher
{
    public const string Argument = "--admin";
    private const int ErrorCancelledByUser = 1223;
    private static string Title => Loc.T("Admin settings", "Admin beállítások");

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LolScrollLimiter", "limits.json");

    /// <summary>Újraindítja a programot emelt jogosultsággal. Jóváhagyás nélkül semmi sem nyílik meg.</summary>
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

    /// <summary>Emelt (rendszergazdai) jogosultsággal fut-e a folyamat.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Az emelt folyamat belépési pontja (<c>Limiter.Tray.exe --admin</c>).</summary>
    public static void RunElevated()
    {
        if (!IsElevated())
        {
            // Jóváhagyás nélkül (pl. kézzel, parancssorból) indítva sem nyílik meg, és úgysem tudna írni.
            MessageBox.Show(Loc.T("The admin panel can only be opened with administrator rights.\nUse the \"Admin settings\" button of the tray application.",
                                  "Az admin felület csak rendszergazdai jogosultsággal nyitható meg.\nHasználd a tálcaalkalmazás „Admin beállítások” gombját."),
                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, @"Local\LolScrollLimiterAdmin", out var created);
        if (!created) return;

        // Második kapu: az admin jelszó (a Windows rendszergazdai jóváhagyás mellett).
        if (!AdminGate.Unlock(null)) return;

        Application.Run(new AdminForm(SettingsPath));
    }
}
