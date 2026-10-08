namespace Limiter.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Munkamenetenként egy példány.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\LolScrollLimiterTray", out var created);
        if (!created) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
