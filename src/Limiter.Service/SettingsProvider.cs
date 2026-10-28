using System.Text.Json;
using Limiter.Core;

namespace Limiter.Service;

/// <summary>
/// Reading limits.json. The file is in the data folder, so only an administrator can change it.
/// For a faulty file the last valid (or default) limit stays in effect, and a warning is shown.
/// </summary>
public sealed class SettingsProvider
{
    private readonly DataPaths _paths;
    private readonly ILogger<SettingsProvider> _log;
    private DateTime _lastWrite = DateTime.MinValue;
    private volatile LimitSettings _current = LimitSettings.Default;

    public SettingsProvider(DataPaths paths, ILogger<SettingsProvider> log)
    {
        _paths = paths;
        _log = log;
        if (!File.Exists(paths.SettingsPath))
            File.WriteAllText(paths.SettingsPath, LimitSettings.Default.ToJson());
        Reload();
    }

    public LimitSettings Current => _current;

    public string? Warning { get; private set; }

    public void Reload()
    {
        try
        {
            var write = File.GetLastWriteTimeUtc(_paths.SettingsPath);
            if (write == _lastWrite) return;
            _current = LimitSettings.Parse(File.ReadAllText(_paths.SettingsPath));
            _lastWrite = write;
            Warning = null;
            _log.LogInformation("Limits loaded: {Settings}", _current);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Warning = Loc.T("limits.json cannot be read; the previous limits stay in effect.", "A limits.json nem olvasható, az előző korlátok maradnak érvényben.");
            _log.LogWarning(ex, "Loading limits.json failed");
        }
    }
}
