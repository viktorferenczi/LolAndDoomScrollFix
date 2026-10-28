using System.Globalization;
using Limiter.Core;

namespace Limiter.Tray;

/// <summary>
/// Protection by the admin password: needed to open the admin panel and to uninstall the program. The password verifier
/// (admin.json) is readable only by administrators and SYSTEM, so every check runs in an elevated process.
/// </summary>
public static class AdminGate
{
    public const string VerifyArgument = "--verify-password";
    public const string SetFileArgument = "--set-password-file";

    // The uninstaller (Inno Setup) expects these exit codes.
    public const int ExitAllowed = 0;
    public const int ExitDenied = 1;
    public const int ExitFileDamaged = 2;
    public const int ExitFailed = 3;

    private const int MaxAttempts = 5;
    private static readonly TimeSpan WrongPasswordDelay = TimeSpan.FromSeconds(1);

    private static string Title => Loc.T("Admin password", "Admin jelszó");

    /// <summary>
    /// Gate of the admin panel. If there is no password yet (updated from an older version), the first opening asks for a new one.
    /// </summary>
    public static bool Unlock(IWin32Window? owner, string? path = null)
    {
        path ??= AdminPassword.DefaultPath;
        switch (AdminPassword.Load(path, out var record))
        {
            case PasswordFileState.Missing:
                return SetNew(owner, path,
                    Loc.T("No admin password is set yet. Enter one: it will be needed for the admin settings and for uninstalling the program.",
                          "Még nincs admin jelszó beállítva. Adj meg egyet: ez kell majd az admin beállításokhoz és a program eltávolításához."));
            case PasswordFileState.Corrupt:
                MessageBox.Show(owner, DamagedMessage(path), Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            default:
                return Prompt(owner, record!, Loc.T("Enter the admin password to open the admin settings.", "Az admin beállítások megnyitásához add meg az admin jelszót."));
        }
    }

    /// <summary>Sets a new password (with confirmation); replaces the existing one.</summary>
    public static bool SetNew(IWin32Window? owner, string? path = null, string? prompt = null)
    {
        path ??= AdminPassword.DefaultPath;
        prompt ??= Loc.T("Enter the new admin password.", "Add meg az új admin jelszót.");
        using var form = new PasswordForm(Title, prompt, confirm: true);
        if (form.ShowDialog(owner) != DialogResult.OK) return false;
        try
        {
            AdminPassword.Save(path, AdminPassword.Create(form.Password));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(owner, Loc.T("Saving the password failed: ", "A jelszó mentése nem sikerült: ") + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>For the uninstaller: exit code (<see cref="ExitAllowed"/> = may proceed). An installation without a password has no obstacle.</summary>
    public static int VerifyForUninstall(string? path = null)
    {
        path ??= AdminPassword.DefaultPath;
        // A non-elevated process cannot read the file: nothing is let through then.
        if (!AdminLauncher.IsElevated()) return ExitDenied;

        switch (AdminPassword.Load(path, out var record))
        {
            case PasswordFileState.Missing:
                return ExitAllowed;
            case PasswordFileState.Corrupt:
                MessageBox.Show(DamagedMessage(path), Loc.T("Uninstall", "Eltávolítás"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return ExitFileDamaged;
            default:
                return Prompt(null, record!, Loc.T("Enter the admin password to uninstall the program.", "A program eltávolításához add meg az admin jelszót.")) ? ExitAllowed : ExitDenied;
        }
    }

    /// <summary>
    /// Sets the password from the temporary file handed over by the installer (hexadecimal character codes), then deletes the file.
    /// </summary>
    public static int SetPasswordFromFile(string file, string? path = null)
    {
        path ??= AdminPassword.DefaultPath;
        try
        {
            var password = AdminPassword.DecodeTransfer(File.ReadAllText(file));
            File.Delete(file);
            if (password is null || AdminPassword.Validate(password) is not null) return ExitFailed;
            AdminPassword.Save(path, AdminPassword.Create(password));
            return ExitAllowed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ExitFailed;
        }
    }

    private static bool Prompt(IWin32Window? owner, PasswordRecord record, string prompt)
    {
        string? notice = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var form = new PasswordForm(Title, prompt, confirm: false, notice);
            if (form.ShowDialog(owner) != DialogResult.OK) return false;
            if (AdminPassword.Verify(form.Password, record)) return true;

            Thread.Sleep(WrongPasswordDelay); // slows down guessing
            notice = Loc.T($"Wrong password ({attempt}/{MaxAttempts}).", $"Hibás jelszó ({attempt}/{MaxAttempts}).");
        }
        MessageBox.Show(owner, Loc.T("Too many failed attempts.", "Túl sok sikertelen próbálkozás."), Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private static string DamagedMessage(string path) =>
        Loc.T("The password file is unreadable or damaged, so the password cannot be verified.\n\n",
              "A jelszófájl nem olvasható vagy sérült, ezért a jelszó nem ellenőrizhető.\n\n") +
        Loc.T($"As an administrator, delete this file: {path}\nThen you can set a new password.",
              $"Rendszergazdaként töröld ezt a fájlt: {path}\nUtána új jelszót állíthatsz be.");
}
