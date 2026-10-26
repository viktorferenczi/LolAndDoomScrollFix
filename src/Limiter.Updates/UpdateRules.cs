using System.Text.RegularExpressions;

namespace Limiter.Updates;

/// <summary>Frissítési szabályok: verziók értelmezése és összehasonlítása, megbízható letöltési címek.</summary>
public static partial class UpdateRules
{
    public const string DefaultRepository = "viktorferenczi/LolAndDoomScrollFix";

    /// <summary>A telepítő fájlnév-mintája, pl. LolScrollLimiter-Setup-1.4.0.exe.</summary>
    [GeneratedRegex(@"^LolScrollLimiter-Setup-(\d+\.\d+\.\d+)\.exe$")]
    public static partial Regex InstallerNamePattern();

    public const string ChecksumFileName = "SHA256SUMS.txt";

    /// <summary>Legfeljebb ekkora telepítőt töltünk le.</summary>
    public const long MaxInstallerBytes = 500L * 1024 * 1024;

    /// <summary>„v1.3.2”, „1.3.2”, „1.3.2.0”, „1.3.2+abc123” → 1.3.2. Más formátumot nem fogad el.</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        var plus = t.IndexOf('+');
        if (plus >= 0) t = t[..plus];
        if (!Regex.IsMatch(t, @"^\d+\.\d+(\.\d+){0,2}$") || !Version.TryParse(t, out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>Három tagúra normalizál (1.3 → 1.3.0, 1.3.2.0 → 1.3.2), hogy az összehasonlítás pontos legyen.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Csak szigorúan újabb verzió ajánlható (visszalépés és azonos verzió nem).</summary>
    public static bool IsNewer(Version current, Version candidate) => Normalize(candidate) > Normalize(current);

    public static string InstallerFileName(Version v) => $"LolScrollLimiter-Setup-{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}.exe";

    /// <summary>
    /// Csak a tároló saját kiadásaiból (https://github.com/{repo}/releases/download/…) tölthetünk le.
    /// </summary>
    public static bool IsTrustedAssetUrl(string? url, string repository)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps
               && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Az átirányítás után is csak GitHub-tárhelyen lehetünk (a kiadási fájlok ott élnek).</summary>
    public static bool IsTrustedFinalHost(Uri? uri) =>
        uri is { Scheme: "https" } && (HostIs(uri.Host, "github.com") || HostIs(uri.Host, "githubusercontent.com"));

    private static bool HostIs(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    /// <summary>A „SHA256SUMS” formátumú fájlból (hash  fájlnév) a megadott fájl ellenőrzőösszege, vagy null.</summary>
    public static string? FindChecksum(string checksumFileText, string fileName)
    {
        foreach (var rawLine in checksumFileText.Split('\n'))
        {
            var line = rawLine.Trim();
            var match = ChecksumLine().Match(line);
            if (!match.Success) continue;
            var name = match.Groups[2].Value.TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.Ordinal)) return match.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    [GeneratedRegex(@"^([0-9a-fA-F]{64})\s+(.+)$")]
    private static partial Regex ChecksumLine();

    /// <summary>A GitHub „digest” mezője („sha256:…”) → hexa összeg, vagy null.</summary>
    public static string? ParseDigest(string? digest)
    {
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[7..].Trim();
        return Regex.IsMatch(hex, "^[0-9a-fA-F]{64}$") ? hex.ToLowerInvariant() : null;
    }
}
