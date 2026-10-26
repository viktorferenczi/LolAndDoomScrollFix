using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Limiter.Updates;

public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Notes,
    string PageUrl,
    string InstallerName,
    string InstallerUrl,
    long InstallerSize,
    string Sha256);

public enum UpdateStatus
{
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(UpdateStatus Status, Version Current, ReleaseInfo? Release = null, string? Error = null);

/// <summary>
/// Frissítések keresése a GitHub-kiadások között, és a telepítő letöltése ellenőrzőösszeggel.
/// A kliens soha nem telepít semmit: a letöltött telepítőt a hívó indítja el, és az rendszergazdai jóváhagyást kér.
/// Az ellenőrzés hibája nem érinti a korlátozások működését.
/// </summary>
public sealed class UpdateClient
{
    private readonly HttpClient _http;
    private readonly string _repository;

    public UpdateClient(HttpClient http, string repository = UpdateRules.DefaultRepository)
    {
        _http = http;
        _repository = repository;
    }

    /// <summary>Alapértelmezett HTTP-kliens a szükséges fejlécekkel és időkorláttal.</summary>
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LolScrollLimiter-Updater/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync($"https://api.github.com/repos/{_repository}/releases/latest", ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Failed(current, "Még nincs közzétett kiadás, vagy a kiadások nem nyilvánosak.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Failed(current, "A GitHub átmenetileg korlátozza a lekérdezéseket. Próbáld újra később.");
            if (!response.IsSuccessStatusCode)
                return Failed(current, $"A GitHub hibát jelzett (HTTP {(int)response.StatusCode}).");

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return await EvaluateAsync(current, json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return Failed(current, ex is TaskCanceledException ? "Időtúllépés a frissítés keresésekor." : "Nincs kapcsolat a GitHubbal: " + ex.Message);
        }
    }

    /// <summary>Kiértékeli a „latest release” választ. Itt dől el, hogy van-e telepíthető, ellenőrizhető újabb verzió.</summary>
    public async Task<UpdateCheckResult> EvaluateAsync(Version current, string releaseJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return Failed(current, "Érthetetlen válasz a GitHubtól.");
        if (Bool(root, "draft") || Bool(root, "prerelease")) return new UpdateCheckResult(UpdateStatus.UpToDate, current);

        var tag = Str(root, "tag_name");
        if (!UpdateRules.TryParseVersion(tag, out var version)) return Failed(current, $"Ismeretlen verziócímke: „{tag}”.");
        if (!UpdateRules.IsNewer(current, version)) return new UpdateCheckResult(UpdateStatus.UpToDate, current);

        // Telepítő és ellenőrzőösszeg keresése a kiadás fájljai között.
        string? installerName = null, installerUrl = null, digest = null, checksumUrl = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Str(asset, "name");
                var url = Str(asset, "browser_download_url");
                if (name is null) continue;
                if (name == UpdateRules.InstallerFileName(version))
                {
                    installerName = name;
                    installerUrl = url;
                    digest = UpdateRules.ParseDigest(Str(asset, "digest"));
                    size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                }
                else if (name == UpdateRules.ChecksumFileName)
                {
                    checksumUrl = url;
                }
            }
        }

        if (installerName is null || installerUrl is null)
            return Failed(current, $"A(z) {version} kiadásban nincs telepítő ({UpdateRules.InstallerFileName(version)}).");
        if (!UpdateRules.IsTrustedAssetUrl(installerUrl, _repository))
            return Failed(current, "A telepítő letöltési címe nem megbízható, ezért nem töltjük le.");
        if (size > UpdateRules.MaxInstallerBytes)
            return Failed(current, "A telepítő a megengedettnél nagyobb.");

        var sha = digest;
        if (sha is null && checksumUrl is not null && UpdateRules.IsTrustedAssetUrl(checksumUrl, _repository))
        {
            using var response = await _http.GetAsync(checksumUrl, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && UpdateRules.IsTrustedFinalHost(response.RequestMessage?.RequestUri))
                sha = UpdateRules.FindChecksum(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), installerName);
        }
        if (sha is null)
            return Failed(current, "A kiadáshoz nem tartozik ellenőrzőösszeg, ezért nem telepíthető biztonságosan.");

        return new UpdateCheckResult(UpdateStatus.Available, current, new ReleaseInfo(
            version, tag!, Str(root, "body") ?? "", Str(root, "html_url") ?? $"https://github.com/{_repository}/releases",
            installerName, installerUrl, size, sha));
    }

    /// <summary>
    /// Letölti a telepítőt a megadott mappába, közben számolja az SHA-256-ot. Csak egyező ellenőrzőösszeg esetén
    /// adja vissza a végleges fájl útvonalát; különben törli a fájlt, és kivételt dob.
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, string directory, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!UpdateRules.IsTrustedAssetUrl(release.InstallerUrl, _repository))
            throw new InvalidDataException("A telepítő letöltési címe nem megbízható.");
        if (!UpdateRules.InstallerNamePattern().IsMatch(release.InstallerName))
            throw new InvalidDataException("A telepítő fájlneve érvénytelen.");

        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, release.InstallerName);
        var partPath = finalPath + ".part";

        using var response = await _http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (!UpdateRules.IsTrustedFinalHost(response.RequestMessage?.RequestUri))
            throw new InvalidDataException("A letöltés nem megbízható címre irányított át.");

        var total = response.Content.Headers.ContentLength ?? release.InstallerSize;
        if (total > UpdateRules.MaxInstallerBytes)
            throw new InvalidDataException("A telepítő a megengedettnél nagyobb.");

        try
        {
            using var hash = SHA256.Create();
            long written = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > UpdateRules.MaxInstallerBytes) throw new InvalidDataException("A telepítő a megengedettnél nagyobb.");
                    hash.TransformBlock(buffer, 0, read, null, 0);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)written / total));
                }
                hash.TransformFinalBlock([], 0, 0);
            }

            var actual = Convert.ToHexString(hash.Hash!).ToLowerInvariant();
            if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A letöltött telepítő ellenőrzőösszege nem egyezik, ezért nem indítjuk el.");
            if (release.InstallerSize > 0 && written != release.InstallerSize)
                throw new InvalidDataException("A letöltött telepítő mérete nem egyezik a kiadáséval.");

            File.Move(partPath, finalPath, overwrite: true);
            progress?.Report(1.0);
            return finalPath;
        }
        catch
        {
            try { File.Delete(partPath); } catch (IOException) { }
            throw;
        }
    }

    private static UpdateCheckResult Failed(Version current, string error) => new(UpdateStatus.Failed, current, Error: error);

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
