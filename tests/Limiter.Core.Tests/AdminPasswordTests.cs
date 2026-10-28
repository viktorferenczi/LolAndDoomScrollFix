namespace Limiter.Core.Tests;

public class AdminPasswordTests
{
    // A fast round count for the tests; the production value is the default.
    private const int Fast = 1_000;

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "limiter-pw-" + Guid.NewGuid().ToString("N"), "admin.json");

    private static void Cleanup(string path)
    {
        try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("      ", false)]
    [InlineData("abc12", false)]
    [InlineData("abc123", true)]
    [InlineData("jelszó-árvíztűrő", true)]
    public void Validate_enforces_length_and_content(string? password, bool ok) =>
        Assert.Equal(ok, AdminPassword.Validate(password) is null);

    [Fact]
    public void Validate_rejects_too_long_passwords()
    {
        Assert.Null(AdminPassword.Validate(new string('a', AdminPassword.MaxLength)));
        Assert.NotNull(AdminPassword.Validate(new string('a', AdminPassword.MaxLength + 1)));
    }

    [Fact]
    public void The_right_password_verifies_and_a_wrong_one_does_not()
    {
        var record = AdminPassword.Create("titok-123", Fast);
        Assert.True(AdminPassword.Verify("titok-123", record));
        Assert.False(AdminPassword.Verify("titok-124", record));
        Assert.False(AdminPassword.Verify("Titok-123", record));
        Assert.False(AdminPassword.Verify("", record));
        Assert.False(AdminPassword.Verify(null, record));
    }

    [Fact]
    public void Unicode_passwords_match_regardless_of_composition()
    {
        var composed = "árvíztűrő";           // precomposed accented letters
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        var record = AdminPassword.Create(composed, Fast);
        Assert.True(AdminPassword.Verify(decomposed, record));
    }

    [Fact]
    public void The_record_never_contains_the_password_and_uses_a_fresh_salt()
    {
        var a = AdminPassword.Create("titok-123", Fast);
        var b = AdminPassword.Create("titok-123", Fast);
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
        Assert.DoesNotContain("titok", a.Salt + a.Hash);
    }

    [Fact]
    public void Create_rejects_a_weak_password()
    {
        Assert.Throws<ArgumentException>(() => AdminPassword.Create("abc", Fast));
    }

    [Fact]
    public void Save_and_Load_round_trip()
    {
        var path = TempFile();
        try
        {
            Assert.Equal(PasswordFileState.Missing, AdminPassword.Load(path, out _));
            AdminPassword.Save(path, AdminPassword.Create("titok-123", Fast));
            Assert.Equal(PasswordFileState.Valid, AdminPassword.Load(path, out var loaded));
            Assert.True(AdminPassword.Verify("titok-123", loaded!));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp-*"));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Saving_again_replaces_the_password()
    {
        var path = TempFile();
        try
        {
            AdminPassword.Save(path, AdminPassword.Create("regi-jelszo", Fast));
            AdminPassword.Save(path, AdminPassword.Create("uj-jelszo", Fast));
            AdminPassword.Load(path, out var loaded);
            Assert.False(AdminPassword.Verify("regi-jelszo", loaded!));
            Assert.True(AdminPassword.Verify("uj-jelszo", loaded!));
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData("nem json")]
    [InlineData("{}")]
    [InlineData("""{"version":1,"iterations":1000,"salt":"AAAA","hash":"AAAA"}""")]
    [InlineData("""{"version":2,"iterations":1000,"salt":"AAAAAAAAAAAAAAAAAAAAAA==","hash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="}""")]
    [InlineData("""{"version":1,"iterations":1,"salt":"AAAAAAAAAAAAAAAAAAAAAA==","hash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="}""")]
    [InlineData("""{"version":1,"iterations":1000,"salt":"***","hash":"***"}""")]
    [InlineData("")]
    public void A_damaged_file_is_corrupt_not_missing(string content)
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            Assert.Equal(PasswordFileState.Corrupt, AdminPassword.Load(path, out var record));
            Assert.Null(record);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Verify_fails_closed_on_a_malformed_record()
    {
        Assert.False(AdminPassword.Verify("titok-123", new PasswordRecord()));
        Assert.False(AdminPassword.Verify("titok-123", new PasswordRecord { Iterations = Fast, Salt = "x", Hash = "y" }));
    }

    [Theory]
    [InlineData("0061006200630031", "abc1")]
    [InlineData("00e1", "á")]
    [InlineData("  00e1\r\n", "á")]            // a trailing line break is allowed
    [InlineData("0151", "ő")]
    public void Transfer_decoding_restores_the_characters(string hex, string expected) =>
        Assert.Equal(expected, AdminPassword.DecodeTransfer(hex));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("006")]       // not a multiple of four
    [InlineData("00zz")]
    public void Transfer_decoding_rejects_garbage(string? hex) => Assert.Null(AdminPassword.DecodeTransfer(hex));
}
