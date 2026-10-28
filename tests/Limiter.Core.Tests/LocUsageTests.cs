using System.Text.RegularExpressions;

namespace Limiter.Core.Tests;

/// <summary>Source scan: the first argument of every <c>Loc.T</c> call is English, the second is not a copy of it.</summary>
public partial class LocUsageTests
{
    [GeneratedRegex("""Loc\.T\(\s*(\$?"(?:[^"\\]|\\.)*")\s*,\s*(\$?"(?:[^"\\]|\\.)*")""", RegexOptions.Singleline)]
    private static partial Regex Call();

    private static IEnumerable<(string File, string En, string Hu)> Calls()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LolScrollLimiter.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir.FullName, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in Call().Matches(File.ReadAllText(file)))
                yield return (Path.GetFileName(file), m.Groups[1].Value, m.Groups[2].Value);
        }
    }

    [Fact]
    public void There_are_many_localized_texts()
    {
        Assert.True(Calls().Count() > 100);
    }

    [Fact]
    public void The_first_argument_is_English()
    {
        var bad = Calls().Where(c => Regex.IsMatch(c.En, "[áéíóöőúüűÁÉÍÓÖŐÚÜŰ]")).Select(c => $"{c.File}: {c.En}").ToList();
        Assert.True(bad.Count == 0, "Hungarian text in the English argument:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void The_second_argument_is_not_a_copy_of_the_first()
    {
        // Identical texts are fine only for names that are the same in both languages.
        var allowed = new[] { "\"LoL PvP\"", "\"LoL PvP: –\"", "\"Messenger\"", "\"LoL\"" };
        var bad = Calls().Where(c => c.En == c.Hu && !allowed.Contains(c.En)).Select(c => $"{c.File}: {c.En}").ToList();
        Assert.True(bad.Count == 0, "Untranslated text:\n" + string.Join("\n", bad));
    }
}
