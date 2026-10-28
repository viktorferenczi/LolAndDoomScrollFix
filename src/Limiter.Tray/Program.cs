namespace Limiter.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Helper commands of the uninstaller and the installer: they do not start a tray icon.
        if (args.Length > 0 && args[0].Equals(AdminGate.VerifyArgument, StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(AdminGate.VerifyForUninstall());
            return;
        }
        if (args.Length > 1 && args[0].Equals(AdminGate.SetFileArgument, StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(AdminGate.SetPasswordFromFile(args[1]));
            return;
        }

        // Admin panel: a separate instance started with elevated rights (the tray application does not run in it).
        if (args.Contains(AdminLauncher.Argument, StringComparer.OrdinalIgnoreCase))
        {
            AdminLauncher.RunElevated();
            return;
        }

        // One instance per session.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\LolScrollLimiterTray", out var created);
        if (!created) return;

        Application.Run(new TrayContext());
    }
}
