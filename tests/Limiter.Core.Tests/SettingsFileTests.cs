namespace Limiter.Core.Tests;

public class SettingsFileTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "limiter-settings-" + Guid.NewGuid().ToString("N"), "limits.json");

    private static void Cleanup(string path)
    {
        try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch (IOException) { }
    }

    private static LimitSettings Everything() => new()
    {
        ResetMode = ResetMode.Daily,
        DailyResetHour = 4,
        LolPvpMatches = 5,
        ShortVideoMinutes = 45,
        FacebookFeedMinutes = 20,
        EnforceOnWeekends = true,
        LolRestDayBonusMatches = 2,
        ShortVideoRestDayBonusMinutes = 15,
        FacebookFeedRestDayBonusMinutes = 0,
        LolBannedHours = new BannedHours { Enabled = true, From = 1, To = 5 },
        ShortVideoBannedHours = new BannedHours { Enabled = true, From = 22, To = 6 },
        FacebookFeedBannedHours = BannedHours.Disabled,
    };

    [Fact]
    public void Default_and_full_settings_are_valid() 
    {
        Assert.Empty(SettingsFile.Validate(LimitSettings.Default));
        Assert.Empty(SettingsFile.Validate(Everything()));
    }

    [Fact]
    public void Reports_every_out_of_range_value_in_hungarian()
    {
        var bad = LimitSettings.Default with
        {
            DailyResetHour = 24,
            LolPvpMatches = 1001,
            ShortVideoMinutes = -1,
            FacebookFeedMinutes = 1441,
            LolRestDayBonusMatches = 11,
            ShortVideoRestDayBonusMinutes = 1441,
            FacebookFeedRestDayBonusMinutes = -5,
            LolBannedHours = new BannedHours { Enabled = true, From = 3, To = 3 },
            ShortVideoBannedHours = new BannedHours { Enabled = true, From = 24, To = 2 },
        };
        var errors = SettingsFile.Validate(bad);
        Assert.Equal(9, errors.Count);
        Assert.All(errors, e => Assert.EndsWith(".", e));
    }

    [Fact]
    public void Equal_ban_hours_are_only_an_error_when_the_ban_is_enabled()
    {
        var off = LimitSettings.Default with { LolBannedHours = new BannedHours { Enabled = false, From = 3, To = 3 } };
        Assert.Empty(SettingsFile.Validate(off));
    }

    [Fact]
    public void Saved_settings_round_trip_through_the_file_the_service_reads()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SettingsFile.Save(path, Everything());
            var (loaded, warning) = SettingsFile.Load(path);
            Assert.Null(warning);
            Assert.Equal(Everything().Sanitized(), loaded);
            // The service uses exactly the same function.
            Assert.Equal(loaded, LimitSettings.Parse(File.ReadAllText(path)));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Save_replaces_an_existing_file_and_leaves_no_temp_files()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "old content");
            SettingsFile.Save(path, LimitSettings.Default with { ShortVideoMinutes = 12 });
            Assert.Equal(12, SettingsFile.Load(path).Settings.ShortVideoMinutes);
            Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Invalid_settings_are_rejected_and_the_old_file_is_untouched()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SettingsFile.Save(path, LimitSettings.Default with { ShortVideoMinutes = 33 });
            var before = File.ReadAllText(path);

            Assert.Throws<ArgumentException>(() => SettingsFile.Save(path, LimitSettings.Default with { ShortVideoMinutes = 5000 }));
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Load_handles_missing_and_broken_files_without_throwing()
    {
        var path = TempFile();
        try
        {
            Assert.Equal((LimitSettings.Default, (string?)null), SettingsFile.Load(path));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ nem json");
            var (settings, warning) = SettingsFile.Load(path);
            Assert.Equal(LimitSettings.Default, settings);
            Assert.NotNull(warning);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Old_file_without_newer_fields_loads_with_defaults_and_is_valid()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{ "lolPvpMatches": 3, "shortVideoMinutes": 60, "facebookFeedMinutes": 60 }""");
            var (settings, warning) = SettingsFile.Load(path);
            Assert.Null(warning);
            Assert.Empty(SettingsFile.Validate(settings));
            Assert.False(settings.LolBannedHours.Enabled);
        }
        finally { Cleanup(path); }
    }
}
