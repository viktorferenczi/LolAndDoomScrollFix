using System.Globalization;
using Limiter.Protocol;

namespace Limiter.Tray;

public enum Health
{
    Ok,
    Warning,
    Error,
}

/// <summary>A szolgáltatás állapotának magyar nyelvű megjelenítése.</summary>
public sealed record StatusView(
    Health Health,
    string ProtectionText,
    string LolText,
    string ShortVideoText,
    string FacebookFeedText,
    string NextReleaseText,
    string ScheduleText,
    IReadOnlyList<string> Problems)
{
    /// <summary>Ennyi ideig jelentkezés nélkül a bővítményt hibásnak tekintjük, ha a Chrome fut.</summary>
    public static readonly TimeSpan ExtensionSilenceLimit = TimeSpan.FromSeconds(90);

    private static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");

    public static StatusView ServiceUnreachable(string error) => new(
        Health.Error,
        "Hiba: a korlátozó szolgáltatás nem érhető el",
        "LoL PvP: –",
        "Rövid videók: –",
        "Facebook-hírfolyam: –",
        "",
        "",
        [$"A korlátozó szolgáltatás nem érhető el ({error}). A védelem most nem működik; a program automatikusan újrapróbálja."]);

    public static StatusView From(IpcResponse s, bool chromeRunningLongEnough, DateTimeOffset now)
    {
        var problems = new List<string>();
        var health = Health.Ok;
        var p = s.Protection ?? new ProtectionDto();

        switch (p.LcuState)
        {
            case IpcConstants.LcuStates.Error:
            case IpcConstants.LcuStates.Degraded:
                problems.Add(p.LcuMessage ?? "Hiba a LoL-kliens kapcsolatában.");
                break;
        }

        if (chromeRunningLongEnough &&
            (p.LastExtensionPingAt is not { } ping || now - ping > ExtensionSilenceLimit))
        {
            problems.Add("A Chrome fut, de a korlátozó bővítmény nem jelentkezik — a webes korlát most nem érvényesül.");
        }

        problems.AddRange(p.Warnings);
        if (problems.Count > 0) health = Health.Warning;

        var lol = s.Lol;
        var exempt = s.Schedule?.ExemptNow == true;
        var lolText = lol is null
            ? "LoL PvP: –"
            : exempt
                ? "LoL PvP: hétvége — nincs korlát"
            : lol.Banned
                ? $"LoL PvP: tiltott időszak ({Range(s.Schedule?.LolBannedHours)}) — újra: {When(lol.AvailableAgainAt, now)}"
            : lol.Blocked
                ? $"LoL PvP: elfogyott ({lol.Used}/{lol.Limit}) — újra: {When(lol.AvailableAgainAt, now)}"
                : $"LoL PvP: {lol.Remaining} meccs maradt ({lol.Limit}-ból{(lol.Bonus > 0 ? $", ebből +{lol.Bonus} pihenőnap-jutalom" : "")})";

        var next = new List<string>();
        if (!exempt)
        {
            if (lol?.NextReleaseAt is { } lr) next.Add($"meccs {When(lr, now)}");
            if (s.ShortVideo?.NextReleaseAt is { } sr) next.Add($"rövid videó {When(sr, now)}");
            if (s.FacebookFeed?.NextReleaseAt is { } fr) next.Add($"hírfolyam {When(fr, now)}");
        }

        return new StatusView(
            health,
            health == Health.Ok ? "Védelem: rendben" : "Figyelmeztetés: " + problems[0],
            lolText,
            Web("Rövid videók", s.ShortVideo, s.Schedule?.ShortVideoBannedHours, now),
            Web("Facebook-hírfolyam", s.FacebookFeed, s.Schedule?.FacebookFeedBannedHours, now),
            exempt ? "Hétvégén nincs korlát"
                : next.Count > 0 ? "Következő felszabadulás: " + string.Join(", ", next) : "Minden keret szabad",
            DescribeSchedule(s.Schedule),
            problems);
    }

    private static string Web(string label, WebLimitDto? w, BannedHoursDto? ban, DateTimeOffset now)
    {
        if (w is null) return $"{label}: –";
        if (w.Exempt) return $"{label}: hétvége — nincs korlát";
        if (w.Banned) return $"{label}: tiltott időszak ({Range(ban)}) — újra: {When(w.AvailableAgainAt, now)}";
        var limitMin = (int)Math.Round(w.LimitSeconds / 60);
        if (w.Blocked) return $"{label}: elfogyott — újra: {When(w.AvailableAgainAt, now)}";
        var bonusMin = (int)Math.Round(w.BonusSeconds / 60);
        var bonus = bonusMin > 0 ? $", ebből +{bonusMin} perc pihenőnap-jutalom" : "";
        return $"{label}: {Minutes(w.RemainingSeconds)} maradt ({limitMin} percből{bonus})";
    }

    /// <summary>A telepítéskor választott időbeosztás szövegesen.</summary>
    public static string DescribeSchedule(ScheduleDto? schedule)
    {
        if (schedule is null) return "";
        var reset = schedule.ResetMode == IpcConstants.ResetModes.Daily
            ? $"A keretek naponta {schedule.DailyResetHour:00}:00-kor nullázódnak."
            : "Minden keret az előző 24 órára vonatkozik; a felhasznált idő 24 órával később fokozatosan felszabadul.";
        var weekend = schedule.EnforceOnWeekends
            ? "A korlátok hétvégén is érvényesek."
            : "Hétvégén (szombat, vasárnap) nincs korlát.";
        var bonuses = new List<string>();
        if (schedule.LolRestDayBonusMatches > 0) bonuses.Add($"LoL +{schedule.LolRestDayBonusMatches} meccs");
        if (schedule.ShortVideoRestDayBonusMinutes > 0) bonuses.Add($"rövid videó +{schedule.ShortVideoRestDayBonusMinutes} perc");
        if (schedule.FacebookFeedRestDayBonusMinutes > 0) bonuses.Add($"hírfolyam +{schedule.FacebookFeedRestDayBonusMinutes} perc");
        var bonus = bonuses.Count > 0
            ? "\nPihenőnap-jutalom: ha egy napon valamiből semmit sem használsz, másnap ahhoz több jár (" +
              string.Join(", ", bonuses) + "); nem halmozódik."
            : "";
        var bans = new List<string>();
        if (schedule.LolBannedHours is { Enabled: true } lb) bans.Add($"LoL {Range(lb)}");
        if (schedule.ShortVideoBannedHours is { Enabled: true } sb) bans.Add($"rövid videó {Range(sb)}");
        if (schedule.FacebookFeedBannedHours is { Enabled: true } fb) bans.Add($"hírfolyam {Range(fb)}");
        var banText = bans.Count > 0
            ? "\nTiltott órák (naponta, a korlátozott napokon): " + string.Join(", ", bans) + "."
            : "";
        return reset + "\n" + weekend + bonus + banText;
    }

    /// <summary>„01:00–05:00”.</summary>
    public static string Range(BannedHoursDto? ban) =>
        ban is null ? "–" : $"{ban.From:00}:00–{ban.To:00}:00";

    public static string Minutes(double seconds)
    {
        var total = (int)Math.Floor(seconds);
        return total >= 60 ? $"{total / 60} perc" : $"{total} mp";
    }

    /// <summary>„ma 14:32”, „holnap 09:10” vagy „okt. 12. 09:10”.</summary>
    public static string When(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } value) return "–";
        var local = value.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var time = local.ToString("HH:mm", Hu);
        if (local.Date == today) return "ma " + time;
        if (local.Date == today.AddDays(1)) return "holnap " + time;
        return local.ToString("MMM d. ", Hu) + time;
    }
}
