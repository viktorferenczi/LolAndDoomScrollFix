using System.Text.RegularExpressions;

namespace Limiter.Core.Tests;

/// <summary>The installer's [CustomMessages] must exist in both languages, with the same placeholders, and every use must be defined.</summary>
public partial class InstallerMessagesTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LolScrollLimiter.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. parts]);
    }

    private static (Dictionary<string, string> En, Dictionary<string, string> Hu, string Source) Load()
    {
        var source = File.ReadAllText(RepoFile("installer", "LolScrollLimiter.iss"));
        var en = new Dictionary<string, string>();
        var hu = new Dictionary<string, string>();
        foreach (Match m in MessageLine().Matches(source))
            (m.Groups[1].Value == "en" ? en : hu)[m.Groups[2].Value] = m.Groups[3].Value.TrimEnd('\r');
        return (en, hu, source);
    }

    [GeneratedRegex(@"^(en|hu)\.(\w+)=(.*)$", RegexOptions.Multiline)]
    private static partial Regex MessageLine();

    [GeneratedRegex(@"CustomMessage\('(\w+)'\)|\{cm:(\w+)\}")]
    private static partial Regex Use();

    [GeneratedRegex(@"%\d")]
    private static partial Regex Placeholder();

    [Fact]
    public void Both_languages_define_the_same_messages()
    {
        var (en, hu, _) = Load();
        Assert.True(en.Count > 30);
        Assert.Equal(en.Keys.Order(), hu.Keys.Order());
    }

    [Fact]
    public void Placeholders_match_between_the_languages()
    {
        var (en, hu, _) = Load();
        foreach (var key in en.Keys)
            Assert.Equal(Placeholder().Matches(en[key]).Select(m => m.Value).Order(), Placeholder().Matches(hu[key]).Select(m => m.Value).Order());
    }

    [Fact]
    public void Every_used_message_is_defined_and_every_definition_is_used()
    {
        var (en, _, source) = Load();
        var used = Use().Matches(source).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToHashSet();
        Assert.All(used, key => Assert.True(en.ContainsKey(key), $"{key} is used but not defined"));
        Assert.All(en.Keys, key => Assert.Contains(key, used));
    }

    [Fact]
    public void English_texts_are_not_copies_of_the_Hungarian_ones()
    {
        var (en, hu, _) = Load();
        var same = en.Keys.Where(k => en[k] == hu[k]).ToList();
        Assert.Empty(same);
    }

    [Fact]
    public void English_texts_contain_no_Hungarian_accented_letters()
    {
        var (en, _, _) = Load();
        // The en dash in time ranges (01:00–05:00) is fine; only Hungarian letters are checked.
        Assert.All(en, pair => Assert.DoesNotMatch("[áéíóöőúüűÁÉÍÓÖŐÚÜŰ]", pair.Value));
    }

    [Fact]
    public void The_installer_lists_English_first_and_Hungarian_second()
    {
        var (_, _, source) = Load();
        var en = source.IndexOf("Name: \"en\"", StringComparison.Ordinal);
        var hu = source.IndexOf("Name: \"hu\"", StringComparison.Ordinal);
        Assert.InRange(en, 0, hu - 1);
    }
}
