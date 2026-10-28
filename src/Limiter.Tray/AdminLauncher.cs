using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Limiter.Tray;

/// <summary>
/// Az admin-felület indítása és védelme. A felületet a program saját magát újraindítva, rendszergazdai jogosultsággal
/// nyitja meg (<c>--admin</c>): a Windows ilyenkor bekéri a rendszergazdai jelszót (UAC), és csak jóváhagyás után jelenik
/// meg az ablak. Ezután az admin jelszót is be kell írni (<see cref="AdminGate"/>); a felhasználó a limits.json-hoz
/// amúgy sem fér hozzá.
/// </summary>
public static class AdminLauncher
{
    public const string Argument = "--admin";
    private const int ErrorCancelledByUser = 1223;
    private const string Title = "Admin beállítások";

    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LolScrollLimiter", "limits.json");

    /// <summary>Újraindítja a programot emelt jogosultsággal. Jóváhagyás nélkül semmi sem nyílik meg.</summary>
    public static void Open(IWin32Window? owner)
    {
        try
        {
            // UseShellExecute + runas: csak így jelenik meg a rendszergazdai jelszót kérő ablak.
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Argument) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledByUser)
        {
            MessageBox.Show(owner,
                "Az admin felület nem nyílt meg: a rendszergazdai jóváhagyás (jelszó) elmaradt.\n" +
                "Kérd meg a rendszergazdai jelszót ismerő személyt.",
                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(owner, "Az admin felület nem indítható: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            MessageBox.Show("Az admin felület csak rendszergazdai jogosultsággal nyitható meg.\n" +
                            "Használd a tálcaalkalmazás „Admin beállítások” gombját.",
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
