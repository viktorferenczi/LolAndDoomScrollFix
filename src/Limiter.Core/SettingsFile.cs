using System.Text;
using System.Text.Json;

namespace Limiter.Core;

/// <summary>A limits.json ellenőrzése, olvasása és biztonságos írása (az admin-felület használja).</summary>
public static class SettingsFile
{
    /// <summary>
    /// Érték-ellenőrzés mentés előtt: a hibákat magyarul adja vissza (üres lista = rendben).
    /// A szolgáltatás betöltéskor szintén határok közé szorít, de a felület ne némán módosítson.
    /// </summary>
    public static IReadOnlyList<string> Validate(LimitSettings s, TimeZoneInfo? zone = null)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(s.ResetMode)) errors.Add("Ismeretlen visszaállítási mód.");
        if (s.DailyResetHour is < 0 or > 23) errors.Add("A napi nullázás órája 0 és 23 között lehet.");
        if (s.LolPvpMatches is < 0 or > 1000) errors.Add("A LoL-meccsek száma 0 és 1000 között lehet.");
        CheckMinutes("A rövid videók keretének", s.ShortVideoMinutes);
        CheckMinutes("A Facebook-hírfolyam keretének", s.FacebookFeedMinutes);
        if (s.LolRestDayBonusMatches is < 0 or > 10) errors.Add("A LoL pihenőnap-jutalma 0 és 10 meccs között lehet.");
        CheckMinutes("A rövid videók jutalmának", s.ShortVideoRestDayBonusMinutes);
        CheckMinutes("A Facebook-hírfolyam jutalmának", s.FacebookFeedRestDayBonusMinutes);
        CheckBan("LoL", s.LolBannedHours);
        CheckBan("Rövid videók", s.ShortVideoBannedHours);
        CheckBan("Facebook-hírfolyam", s.FacebookFeedBannedHours);
        CheckGrants();
        return errors;

        void CheckGrants()
        {
            var seen = new HashSet<DateOnly>();
            var schedule = new LimitSchedule(s, zone ?? TimeZoneInfo.Local);
            foreach (var g in s.ExtraGrants ?? [])
            {
                var day = g.Date.ToString("yyyy-MM-dd");
                if (g.Date == default) errors.Add("Az extra keretnél hiányzik a nap.");
                else if (!seen.Add(g.Date)) errors.Add($"Extra keret: {day} napra több bejegyzés van.");
                if (g.IsEmpty) errors.Add($"Extra keret ({day}): adj meg legalább egy értéket.");
                if (g.LolMatches is < 0 or > DayGrant.MaxMatches)
                    errors.Add($"Extra keret ({day}): a LoL-meccsek száma 0 és {DayGrant.MaxMatches} között lehet.");
                if (g.ShortVideoMinutes is < 0 or > DayGrant.MaxMinutes || g.FacebookFeedMinutes is < 0 or > DayGrant.MaxMinutes)
                    errors.Add($"Extra keret ({day}): a perc 0 és {DayGrant.MaxMinutes} között lehet.");
                if (g.Date != default && schedule.IsExemptDay(g.Date))
                    errors.Add($"Extra keret ({day}): hétvégén nincs korlát, ezért nem adható rá extra. Töröld a bejegyzést, vagy kapcsold be a hétvégi korlátot.");
            }
            if ((s.ExtraGrants?.Count ?? 0) > LimitSettings.MaxGrants)
                errors.Add($"Legfeljebb {LimitSettings.MaxGrants} extra keret adható meg.");
        }

        void CheckMinutes(string what, int minutes)
        {
            if (minutes is < 0 or > 24 * 60) errors.Add($"{what} értéke 0 és 1440 perc között lehet.");
        }

        void CheckBan(string what, BannedHours ban)
        {
            if (ban is null) return;
            if (ban.From is < 0 or > 23 || ban.To is < 0 or > 23)
                errors.Add($"{what}: a tiltott órák 0 és 23 között lehetnek.");
            else if (ban.Enabled && ban.From == ban.To)
                errors.Add($"{what}: a tiltott időszak kezdete és vége nem lehet ugyanaz az óra.");
        }
    }

    /// <summary>Beolvassa a fájlt. Hiányzó fájlnál az alapértékeket adja; hibás fájlnál az alapértékeket és a hibát.</summary>
    public static (LimitSettings Settings, string? Warning) Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return (LimitSettings.Default, null);
            return (LimitSettings.Parse(File.ReadAllText(path)), null);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (LimitSettings.Default, "A meglévő limits.json nem olvasható, ezért az alapértékekkel indul a szerkesztő: " + ex.Message);
        }
    }

    /// <summary>
    /// Érvényes értékeket ír: előbb egy ideiglenes fájlba, majd egy lépésben cseréli a limits.json-t, így a szolgáltatás
    /// sosem olvas félig megírt fájlt. Érvénytelen értéknél kivételt dob, és a fájlhoz nem nyúl.
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
