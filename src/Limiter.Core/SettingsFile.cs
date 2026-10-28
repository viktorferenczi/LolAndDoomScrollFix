using System.Text;
using System.Text.Json;

namespace Limiter.Core;

/// <summary>Validating, reading and safely writing limits.json (used by the admin panel).</summary>
public static class SettingsFile
{
    /// <summary>
    /// Value validation before saving: returns the errors in the active language (empty list = fine).
    /// The service also clamps to the bounds when loading, but the panel must not change values silently.
    /// </summary>
    public static IReadOnlyList<string> Validate(LimitSettings s, TimeZoneInfo? zone = null)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(s.ResetMode)) errors.Add(Loc.T("Unknown reset mode.", "Ismeretlen visszaállítási mód."));
        if (s.DailyResetHour is < 0 or > 23) errors.Add(Loc.T("The daily reset hour must be between 0 and 23.", "A napi nullázás órája 0 és 23 között lehet."));
        if (s.LolPvpMatches is < 0 or > 1000) errors.Add(Loc.T("The number of LoL matches must be between 0 and 1000.", "A LoL-meccsek száma 0 és 1000 között lehet."));
        CheckMinutes(Loc.T("The short-video limit", "A rövid videók keretének"), s.ShortVideoMinutes);
        CheckMinutes(Loc.T("The Facebook feed limit", "A Facebook-hírfolyam keretének"), s.FacebookFeedMinutes);
        if (s.LolRestDayBonusMatches is < 0 or > 10) errors.Add(Loc.T("The LoL rest-day bonus must be between 0 and 10 matches.", "A LoL pihenőnap-jutalma 0 és 10 meccs között lehet."));
        CheckMinutes(Loc.T("The short-video bonus", "A rövid videók jutalmának"), s.ShortVideoRestDayBonusMinutes);
        CheckMinutes(Loc.T("The Facebook feed bonus", "A Facebook-hírfolyam jutalmának"), s.FacebookFeedRestDayBonusMinutes);
        CheckBan("LoL", s.LolBannedHours);
        CheckBan(Loc.T("Short videos", "Rövid videók"), s.ShortVideoBannedHours);
        CheckBan(Loc.T("Facebook feed", "Facebook-hírfolyam"), s.FacebookFeedBannedHours);
        CheckGrants();
        return errors;

        void CheckGrants()
        {
            var seen = new HashSet<DateOnly>();
            var schedule = new LimitSchedule(s, zone ?? TimeZoneInfo.Local);
            foreach (var g in s.ExtraGrants ?? [])
            {
                var day = g.Date.ToString("yyyy-MM-dd");
                if (g.Date == default) errors.Add(Loc.T("An extra allowance has no day.", "Az extra keretnél hiányzik a nap."));
                else if (!seen.Add(g.Date)) errors.Add(Loc.T($"Extra allowance: there is more than one entry for {day}.", $"Extra keret: {day} napra több bejegyzés van."));
                if (g.IsEmpty) errors.Add(Loc.T($"Extra allowance ({day}): enter at least one value.", $"Extra keret ({day}): adj meg legalább egy értéket."));
                if (g.LolMatches is < 0 or > DayGrant.MaxMatches)
                    errors.Add(Loc.T($"Extra allowance ({day}): the number of LoL matches must be between 0 and {DayGrant.MaxMatches}.", $"Extra keret ({day}): a LoL-meccsek száma 0 és {DayGrant.MaxMatches} között lehet."));
                if (g.ShortVideoMinutes is < 0 or > DayGrant.MaxMinutes || g.FacebookFeedMinutes is < 0 or > DayGrant.MaxMinutes)
                    errors.Add(Loc.T($"Extra allowance ({day}): minutes must be between 0 and {DayGrant.MaxMinutes}.", $"Extra keret ({day}): a perc 0 és {DayGrant.MaxMinutes} között lehet."));
                if (g.Date != default && schedule.IsExemptDay(g.Date))
                    errors.Add(Loc.T($"Extra allowance ({day}): there is no limit on weekends, so no extra can be given for it. Delete the entry or turn on the weekend limit.", $"Extra keret ({day}): hétvégén nincs korlát, ezért nem adható rá extra. Töröld a bejegyzést, vagy kapcsold be a hétvégi korlátot."));
            }
            if ((s.ExtraGrants?.Count ?? 0) > LimitSettings.MaxGrants)
                errors.Add(Loc.T($"At most {LimitSettings.MaxGrants} extra allowances can be given.", $"Legfeljebb {LimitSettings.MaxGrants} extra keret adható meg."));
        }

        void CheckMinutes(string what, int minutes)
        {
            if (minutes is < 0 or > 24 * 60) errors.Add(Loc.T($"{what} must be between 0 and 1440 minutes.", $"{what} értéke 0 és 1440 perc között lehet."));
        }

        void CheckBan(string what, BannedHours ban)
        {
            if (ban is null) return;
            if (ban.From is < 0 or > 23 || ban.To is < 0 or > 23)
                errors.Add(Loc.T($"{what}: banned hours must be between 0 and 23.", $"{what}: a tiltott órák 0 és 23 között lehetnek."));
            else if (ban.Enabled && ban.From == ban.To)
                errors.Add(Loc.T($"{what}: the start and the end of the banned period cannot be the same hour.", $"{what}: a tiltott időszak kezdete és vége nem lehet ugyanaz az óra."));
        }
    }

    /// <summary>Reads the file. For a missing file it returns the defaults; for a faulty file the defaults and the error.</summary>
    public static (LimitSettings Settings, string? Warning) Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return (LimitSettings.Default, null);
            return (LimitSettings.Parse(File.ReadAllText(path)), null);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (LimitSettings.Default, Loc.T("The existing limits.json cannot be read, so the editor starts with the defaults: ", "A meglévő limits.json nem olvasható, ezért az alapértékekkel indul a szerkesztő: ") + ex.Message);
        }
    }

    /// <summary>
    /// Writes valid values: first to a temporary file, then replaces limits.json in one step, so the service
    /// never reads a half-written file. For an invalid value it throws and does not touch the file.
    /// </summary>
    public static void Save(string path, LimitSettings settings)
    {
        var errors = Validate(settings);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(settings));

        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, settings.ToJson(), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw;
        }
    }
}
