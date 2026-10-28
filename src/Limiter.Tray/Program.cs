namespace Limiter.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Az eltávolító és a telepítő segédparancsai: nem indítanak tálcaikont.
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

        // Admin-felület: emelt jogosultsággal indított külön példány (a tálcaalkalmazás nem fut benne).
        if (args.Contains(AdminLauncher.Argument, StringComparer.OrdinalIgnoreCase))
        {
            AdminLauncher.RunElevated();
            return;
        }

        // Munkamenetenként egy példány.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\LolScrollLimiterTray", out var created);
        if (!created) return;

        Application.Run(new TrayContext());
    }
}
