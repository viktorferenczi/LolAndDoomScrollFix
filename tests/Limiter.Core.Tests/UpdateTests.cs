using System.Net;
using System.Security.Cryptography;
using System.Text;
using Limiter.Updates;

namespace Limiter.Core.Tests;

public class UpdateTests
{
    private const string Repo = UpdateRules.DefaultRepository;
    private static readonly Version Current = new(1, 3, 2);

    // ---------- verziók ----------

    [Theory]
    [InlineData("v1.3.2", "1.3.2")]
    [InlineData("1.3.2", "1.3.2")]
    [InlineData("V1.4.0", "1.4.0")]
    [InlineData("1.3.2.0", "1.3.2")]
    [InlineData("1.3", "1.3.0")]
    [InlineData("1.3.2+abc123", "1.3.2")]
    public void Parses_versions(string text, string expected)
    {
        Assert.True(UpdateRules.TryParseVersion(text, out var v));
        Assert.Equal(Version.Parse(expected), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.3.2-beta")]
    [InlineData("1.3.2.4.5")]
    [InlineData("v1.x.2")]
    public void Rejects_other_formats(string? text) => Assert.False(UpdateRules.TryParseVersion(text, out _));

    [Theory]
    [InlineData("1.3.2", "1.3.3", true)]
    [InlineData("1.3.2", "1.4.0", true)]
    [InlineData("1.3.2", "2.0.0", true)]
    [InlineData("1.3.2", "1.3.2", false)]
    [InlineData("1.3.2", "1.3.1", false)]
    [InlineData("1.3.2", "1.2.9", false)]
    [InlineData("1.3.2.0", "1.3.2", false)]
    [InlineData("1.3", "1.3.0", false)]
    [InlineData("1.10.0", "1.9.0", false)]
    public void Only_strictly_newer_versions_are_offered(string current, string candidate, bool expected) =>
        Assert.Equal(expected, UpdateRules.IsNewer(Version.Parse(current), Version.Parse(candidate)));

    // ---------- címek és ellenőrzőösszeg ----------

    [Theory]
    [InlineData("https://github.com/viktorferenczi/LolAndDoomScrollFix/releases/download/v1.4.0/LolScrollLimiter-Setup-1.4.0.exe", true)]
    [InlineData("http://github.com/viktorferenczi/LolAndDoomScrollFix/releases/download/v1.4.0/a.exe", false)]
    [InlineData("https://evil.example/viktorferenczi/LolAndDoomScrollFix/releases/download/v1.4.0/a.exe", false)]
    [InlineData("https://github.com.evil.example/viktorferenczi/LolAndDoomScrollFix/releases/download/v1.4.0/a.exe", false)]
    [InlineData("https://github.com/someone-else/LolAndDoomScrollFix/releases/download/v1.4.0/a.exe", false)]
    [InlineData("https://github.com/viktorferenczi/LolAndDoomScrollFix/archive/main.zip", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void Only_the_repositorys_own_release_files_are_trusted(string? url, bool expected) =>
        Assert.Equal(expected, UpdateRules.IsTrustedAssetUrl(url, Repo));

    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("https://github.com/x", true)]
    [InlineData("https://githubusercontent.com.evil.example/x", false)]
    [InlineData("https://evil.example/github.com", false)]
    [InlineData("http://github.com/x", false)]
    public void Redirects_may_only_end_on_github_hosts(string url, bool expected) =>
        Assert.Equal(expected, UpdateRules.IsTrustedFinalHost(new Uri(url)));

    [Fact]
    public void Finds_checksums_in_sha256sum_format()
    {
        var hash = new string('a', 64);
        var other = new string('B', 64);
        var text = $"{other}  other.zip\r\n{hash}  LolScrollLimiter-Setup-1.4.0.exe\r\n{other} *star.exe\n";
        Assert.Equal(hash, UpdateRules.FindChecksum(text, "LolScrollLimiter-Setup-1.4.0.exe"));
        Assert.Equal(other.ToLowerInvariant(), UpdateRules.FindChecksum(text, "star.exe"));
        Assert.Null(UpdateRules.FindChecksum(text, "missing.exe"));
        Assert.Null(UpdateRules.FindChecksum("garbage", "x.exe"));
    }

    [Fact]
    public void Parses_github_digest_field()
    {
        var hex = new string('c', 64);
        Assert.Equal(hex, UpdateRules.ParseDigest("sha256:" + hex.ToUpperInvariant()));
        Assert.Null(UpdateRules.ParseDigest("sha1:" + hex));
        Assert.Null(UpdateRules.ParseDigest("sha256:short"));
        Assert.Null(UpdateRules.ParseDigest(null));
    }

    // ---------- kiadás kiértékelése ----------

    private static string InstallerUrl(string v) => $"https://github.com/{Repo}/releases/download/v{v}/LolScrollLimiter-Setup-{v}.exe";
    private static string SumsUrl(string v) => $"https://github.com/{Repo}/releases/download/v{v}/SHA256SUMS.txt";

    private static string ReleaseJson(string version, string? digest = null, bool withSums = true, bool withInstaller = true,
        string? installerUrl = null, bool draft = false, bool prerelease = false, long size = 5)
    {
        var assets = new List<string>();
        if (withInstaller)
            assets.Add($$"""{ "name": "LolScrollLimiter-Setup-{{version}}.exe", "size": {{size}}, "browser_download_url": "{{installerUrl ?? InstallerUrl(version)}}"{{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }""");
        if (withSums)
            assets.Add($$"""{ "name": "SHA256SUMS.txt", "size": 100, "browser_download_url": "{{SumsUrl(version)}}" }""");
        return $$"""
            { "tag_name": "v{{version}}", "body": "Újdonságok", "html_url": "https://github.com/{{Repo}}/releases/tag/v{{version}}",
              "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
              "assets": [ {{string.Join(",", assets)}} ] }
            """;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            var response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) };
    private static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    private static UpdateClient Client(FakeHandler handler) => new(new HttpClient(handler));

    [Fact]
    public async Task Newer_release_with_digest_is_available()
    {
        var hex = new string('d', 64);
        var result = await Client(new FakeHandler(_ => Ok(""))).EvaluateAsync(Current, ReleaseJson("1.4.0", digest: "sha256:" + hex));
        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal(new Version(1, 4, 0), result.Release!.Version);
        Assert.Equal(hex, result.Release.Sha256);
        Assert.Equal("LolScrollLimiter-Setup-1.4.0.exe", result.Release.InstallerName);
        Assert.Equal("Újdonságok", result.Release.Notes);
    }

    [Fact]
    public async Task Checksum_can_come_from_the_sha256sums_asset()
    {
        var hex = new string('e', 64);
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt")
            ? Ok($"{hex}  LolScrollLimiter-Setup-1.4.0.exe\n") : new HttpResponseMessage(HttpStatusCode.NotFound));
        var result = await Client(handler).EvaluateAsync(Current, ReleaseJson("1.4.0"));
        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal(hex, result.Release!.Sha256);
    }

    [Fact]
    public async Task Same_or_older_or_pre_release_means_up_to_date()
    {
        var client = Client(new FakeHandler(_ => Ok("")));
        foreach (var json in new[]
        {
            ReleaseJson("1.3.2", digest: "sha256:" + new string('a', 64)),
            ReleaseJson("1.3.1", digest: "sha256:" + new string('a', 64)),
            ReleaseJson("1.9.0", digest: "sha256:" + new string('a', 64), prerelease: true),
            ReleaseJson("1.9.0", digest: "sha256:" + new string('a', 64), draft: true),
        })
            Assert.Equal(UpdateStatus.UpToDate, (await client.EvaluateAsync(Current, json)).Status);
    }

    [Fact]
    public async Task Release_without_checksum_is_not_installable()
    {
        var result = await Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))
            .EvaluateAsync(Current, ReleaseJson("1.4.0", withSums: false));
        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("ellenőrzőösszeg", result.Error);
    }

    [Fact]
    public async Task Release_without_installer_or_with_untrusted_url_fails()
    {
        var client = Client(new FakeHandler(_ => Ok("")));
        var hex = "sha256:" + new string('a', 64);
        Assert.Equal(UpdateStatus.Failed, (await client.EvaluateAsync(Current, ReleaseJson("1.4.0", hex, withInstaller: false))).Status);
        Assert.Equal(UpdateStatus.Failed, (await client.EvaluateAsync(Current,
            ReleaseJson("1.4.0", hex, installerUrl: "https://evil.example/LolScrollLimiter-Setup-1.4.0.exe"))).Status);
        Assert.Equal(UpdateStatus.Failed, (await client.EvaluateAsync(Current, ReleaseJson("1.4.0", hex, size: UpdateRules.MaxInstallerBytes + 1))).Status);
    }

    [Fact]
    public async Task Garbage_tag_fails()
    {
        var result = await Client(new FakeHandler(_ => Ok(""))).EvaluateAsync(Current, """{ "tag_name": "nightly", "assets": [] }""");
        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    // ---------- teljes lekérdezés ----------

    [Fact]
    public async Task Check_reports_private_or_missing_repository_clearly()
    {
        var result = await Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))).CheckAsync(Current);
        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("nem nyilvános", result.Error);
    }

    [Fact]
    public async Task Check_handles_rate_limit_server_error_and_network_failure()
    {
        Assert.Contains("korlátozza", (await Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))).CheckAsync(Current)).Error);
        Assert.Contains("500", (await Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))).CheckAsync(Current)).Error);
        var offline = Client(new FakeHandler(_ => throw new HttpRequestException("dns")));
        var result = await offline.CheckAsync(Current);
        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Check_queries_the_repositorys_latest_release_only()
    {
        var handler = new FakeHandler(_ => Ok(ReleaseJson("1.3.2", digest: "sha256:" + new string('a', 64))));
        var result = await Client(handler).CheckAsync(Current);
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Equal([$"https://api.github.com/repos/{Repo}/releases/latest"], handler.Requests);
    }

    // ---------- letöltés ----------

    private static ReleaseInfo Release(byte[] data, string? sha = null, long? size = null) => new(
        new Version(1, 4, 0), "v1.4.0", "", "", "LolScrollLimiter-Setup-1.4.0.exe", InstallerUrl("1.4.0"),
        size ?? data.Length, sha ?? Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "limiter-upd-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Download_with_matching_checksum_returns_the_file()
    {
        var data = Encoding.UTF8.GetBytes("pretend installer bytes");
        var dir = TempDir();
        try
        {
            var progress = new List<double>();
            var path = await Client(new FakeHandler(_ => Bytes(data))).DownloadAsync(Release(data), dir, new Progress<double>(progress.Add));
            Assert.Equal(Path.Combine(dir, "LolScrollLimiter-Setup-1.4.0.exe"), path);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(dir, "*.part"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Download_with_wrong_checksum_is_deleted_and_rejected()
    {
        var data = Encoding.UTF8.GetBytes("tampered installer");
        var dir = TempDir();
        try
        {
            var release = Release(data, sha: new string('0', 64));
            await Assert.ThrowsAsync<InvalidDataException>(() => Client(new FakeHandler(_ => Bytes(data))).DownloadAsync(release, dir));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Download_with_wrong_size_is_rejected()
    {
        var data = Encoding.UTF8.GetBytes("installer");
        var dir = TempDir();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                Client(new FakeHandler(_ => Bytes(data))).DownloadAsync(Release(data, size: data.Length + 10), dir));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Download_rejects_untrusted_urls_names_and_redirects_without_any_request()
    {
        var data = new byte[] { 1, 2, 3 };
        var handler = new FakeHandler(_ => Bytes(data));
        var client = Client(handler);
        var dir = TempDir();

        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release(data) with { InstallerUrl = "https://evil.example/a.exe" }, dir));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release(data) with { InstallerName = "..\\..\\evil.exe" }, dir));
        Assert.Empty(handler.Requests);

        // Átirányítás idegen hosztra: a válasz végső címe nem GitHub.
        var evil = new HttpClient(new RedirectingHandler(data));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateClient(evil).DownloadAsync(Release(data), dir));
        Assert.False(Directory.Exists(dir) && Directory.GetFiles(dir).Length > 0);
    }

    private sealed class RedirectingHandler(byte[] data) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/payload.exe"),
            });
    }
}
