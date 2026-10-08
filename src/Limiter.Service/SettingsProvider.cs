using System.Text.Json;
using Limiter.Core;

namespace Limiter.Service;

/// <summary>
/// A limits.json beolvasása. A fájl az adatmappában van, így csak rendszergazda módosíthatja.
/// Hibás fájlnál az utolsó érvényes (vagy alapértelmezett) korlát marad érvényben, és figyelmeztetés jelenik meg.
/// </summary>
public sealed class SettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly DataPaths _paths;
    private readonly ILogger<SettingsProvider> _log;
    private DateTime _lastWrite = DateTime.MinValue;
    private volatile LimitSettings _current = LimitSettings.Default;

    public SettingsProvider(DataPaths paths, ILogger<SettingsProvider> log)
    {
        _paths = paths;
        _log = log;
        if (!File.Exists(paths.SettingsPath))
            File.WriteAllText(paths.SettingsPath, JsonSerializer.Serialize(LimitSettings.Default, JsonOptions));
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
            var parsed = JsonSerializer.Deserialize<LimitSettings>(File.ReadAllText(_paths.SettingsPath), JsonOptions)
                         ?? throw new JsonException("üres fájl");
            _current = parsed.Sanitized();
            _lastWrite = write;
            Warning = null;
            _log.LogInformation("Korlátok betöltve: {Settings}", _current);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Warning = "A limits.json nem olvasható, az előző korlátok maradnak érvényben.";
            _log.LogWarning(ex, "A limits.json betöltése sikertelen");
        }
    }
}
