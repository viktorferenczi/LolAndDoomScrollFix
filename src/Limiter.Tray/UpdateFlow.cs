using Limiter.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Limiter.Updates;

namespace Limiter.Tray;

/// <summary>
/// "Check for updates": looks up the latest release, downloads and verifies the installer after confirmation, then starts it.
/// The installation asks for administrator approval (UAC) — the trusted person types the password; the program cannot
/// update itself without approval, and this cannot be used to switch the limits off.
/// </summary>
public sealed class UpdateFlow
{
    private static string Title => Loc.T("Check for updates", "Frissítések keresése");
    private const int ErrorCancelledByUser = 1223;

    private readonly UpdateClient _client = new(UpdateClient.CreateHttpClient());
    private bool _running;

    public static Version CurrentVersion { get; } = UpdateRules.Normalize(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public bool IsRunning => _running;

    public event Action? StateChanged;

    public async Task RunAsync(IWin32Window? owner)
    {
        if (_running) return;
        _running = true;
        StateChanged?.Invoke();
        try
        {
            using var wait = new WaitCursor();
            var check = await _client.CheckAsync(CurrentVersion).ConfigureAwait(true);
            wait.Dispose();

            switch (check.Status)
            {
                case UpdateStatus.UpToDate:
                    Info(owner, Loc.T($"The program is up to date (version: {CurrentVersion}).", $"A program naprakész (verzió: {CurrentVersion})."));
                    return;
                case UpdateStatus.Failed:
                    Warn(owner, Loc.T("Checking for updates failed.\n\n", "A frissítések keresése nem sikerült.\n\n") + check.Error);
                    return;
            }

            var release = check.Release!;
            var notes = release.Notes.Length > 600 ? release.Notes[..600] + "…" : release.Notes;
            var ask = MessageBox.Show(owner,
                Loc.T($"A new version is available: {release.Version} (current: {CurrentVersion}).\n\n",
                      $"Új verzió érhető el: {release.Version} (jelenlegi: {CurrentVersion}).\n\n") +
                (notes.Length > 0 ? notes + "\n\n" : "") +
                Loc.T("Download and install it now? Installing needs administrator approval (password). Your settings and counters are kept.",
                      "Letöltöd és telepíted most? A telepítéshez rendszergazdai jóváhagyás (jelszó) kell. A beállításaid és a számlálók megmaradnak."),
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask != DialogResult.Yes) return;

            string installer;
            try
            {
                using var wait2 = new WaitCursor();
                var dir = Path.Combine(Path.GetTempPath(), "LolScrollLimiterUpdate-" + Guid.NewGuid().ToString("N"));
                installer = await _client.DownloadAsync(release, dir).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or TaskCanceledException)
            {
                Warn(owner, Loc.T("The download or the verification failed, so nothing was installed.\n\n", "A letöltés vagy az ellenőrzés nem sikerült, ezért semmi sem települt.\n\n") + ex.Message);
                return;
            }

            Launch(owner, installer);
        }
        finally
        {
            _running = false;
            StateChanged?.Invoke();
        }
    }

    private static void Launch(IWin32Window? owner, string installer)
    {
        try
        {
            // UseShellExecute: the installer asks for administrator rights, and only this way does the approval prompt appear.
            // /UPDATE: update the existing installation keeping the settings; /SILENT: only the progress is visible.
            Process.Start(new ProcessStartInfo(installer, "/UPDATE /SILENT /NORESTART") { UseShellExecute = true });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledByUser)
        {
            Warn(owner, Loc.T("The installation did not start: the administrator approval was not given. Ask the person who knows the administrator password and try again.",
                              "A telepítés nem indult el: a rendszergazdai jóváhagyás elmaradt. Kérd meg a rendszergazdai jelszót ismerő személyt, és próbáld újra."));
        }
        catch (Win32Exception ex)
        {
            Warn(owner, Loc.T("The installer cannot be started: ", "A telepítő nem indítható: ") + ex.Message);
        }
    }

    private static void Info(IWin32Window? owner, string text) =>
        MessageBox.Show(owner, text, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static void Warn(IWin32Window? owner, string text) =>
        MessageBox.Show(owner, text, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private sealed class WaitCursor : IDisposable
    {
        private readonly Cursor? _previous = Cursor.Current;
        private bool _disposed;

        public WaitCursor() => Cursor.Current = Cursors.WaitCursor;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cursor.Current = _previous;
        }
    }
}
