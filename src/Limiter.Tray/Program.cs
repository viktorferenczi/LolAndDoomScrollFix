namespace Limiter.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

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
