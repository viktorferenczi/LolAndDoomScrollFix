namespace Limiter.Core.Tests;

/// <summary>
/// The language is process-wide state, so these tests do not run in parallel with the rest
/// (they share the "Language" collection and always restore English).
/// </summary>
[Collection("Language")]
public class LocalizationTests
{
    private static void WithLanguage(UiLanguage language, Action action)
    {
        var before = Loc.Language;
        Loc.Language = language;
        try { action(); }
        finally { Loc.Language = before; }
    }

    [Fact]
    public void T_picks_the_text_of_the_active_language()
    {
        WithLanguage(UiLanguage.English, () => Assert.Equal("Save", Loc.T("Save", "Mentés")));
        WithLanguage(UiLanguage.Hungarian, () => Assert.Equal("Mentés", Loc.T("Save", "Mentés")));
    }

    [Theory]
    [InlineData("hu", UiLanguage.Hungarian)]
    [InlineData("HU", UiLanguage.Hungarian)]
    [InlineData(" hu ", UiLanguage.Hungarian)]
    [InlineData("en", UiLanguage.English)]
    [InlineData("de", UiLanguage.English)]
    [InlineData("", UiLanguage.English)]
    [InlineData(null, UiLanguage.English)]
    public void Parse_maps_language_codes(string? code, UiLanguage expected) => Assert.Equal(expected, Loc.Parse(code));

    [Fact]
    public void Culture_follows_the_language()
    {
        WithLanguage(UiLanguage.English, () => Assert.Equal("en-US", Loc.Culture.Name));
        WithLanguage(UiLanguage.Hungarian, () => Assert.Equal("hu-HU", Loc.Culture.Name));
    }

    [Fact]
    public void Validation_messages_are_translated()
    {
        var bad = LimitSettings.Default with { LolPvpMatches = 5000 };
        WithLanguage(UiLanguage.English, () => Assert.Contains("between 0 and 1000", Assert.Single(SettingsFile.Validate(bad))));
        WithLanguage(UiLanguage.Hungarian, () => Assert.Contains("0 és 1000 között", Assert.Single(SettingsFile.Validate(bad))));
    }

    [Fact]
    public void Password_messages_are_translated()
    {
        WithLanguage(UiLanguage.English, () => Assert.Contains("at least", AdminPassword.Validate("abc")));
        WithLanguage(UiLanguage.Hungarian, () => Assert.Contains("legalább", AdminPassword.Validate("abc")));
    }
}

[CollectionDefinition("Language", DisableParallelization = true)]
public class LanguageCollection;
