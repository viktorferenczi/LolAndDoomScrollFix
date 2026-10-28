using Limiter.Core;
using Limiter.Protocol;

namespace Limiter.Tray;

public enum Health
{
    Ok,
    Warning,
    Error,
}

/// <summary>Presents the service state as text in the active language.</summary>
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
    /// <summary>If Chrome runs and the extension has not reported for this long, the extension is considered faulty.</summary>
    public static readonly TimeSpan ExtensionSilenceLimit = TimeSpan.FromSeconds(90);

    public static StatusView ServiceUnreachable(string error) => new(
        Health.Error,
        Loc.T("Error: the limiter service is not reachable", "Hiba: a korlátozó szolgáltatás nem érhető el"),
        "LoL PvP: –",
        Loc.T("Short videos: –", "Rövid videók: –"),
        Loc.T("Facebook feed: –", "Facebook-hírfolyam: –"),
        "",
        "",
        [Loc.T($"The limiter service is not reachable ({error}). Protection is not working right now; the program retries automatically.",
               $"A korlátozó szolgáltatás nem érhető el ({error}). A védelem most nem működik; a program automatikusan újrapróbálja.")]);

    public static StatusView From(IpcResponse s, bool chromeRunningLongEnough, DateTimeOffset now)
    {
        var problems = new List<string>();
        var health = Health.Ok;
        var p = s.Protection ?? new ProtectionDto();

        switch (p.LcuState)
        {
            case IpcConstants.LcuStates.Error:
            case IpcConstants.LcuStates.Degraded:
                problems.Add(p.LcuMessage ?? Loc.T("Error in the LoL client connection.", "Hiba a LoL-kliens kapcsolatában."));
                break;
        }

        if (chromeRunningLongEnough &&
            (p.LastExtensionPingAt is not { } ping || now - ping > ExtensionSilenceLimit))
        {
            problems.Add(Loc.T("Chrome is running but the limiter extension is not reporting — the web limit is not enforced right now.", "A Chrome fut, de a korlátozó bővítmény nem jelentkezik — a webes korlát most nem érvényesül."));
        }

        problems.AddRange(p.Warnings);
        if (problems.Count > 0) health = Health.Warning;

        var lol = s.Lol;
        var exempt = s.Schedule?.ExemptNow == true;
        var lolText = lol is null
            ? "LoL PvP: –"
            : exempt
                ? Loc.T("LoL PvP: weekend — no limit", "LoL PvP: hétvége — nincs korlát")
            : lol.Banned
                ? Loc.T($"LoL PvP: banned period ({Range(s.Schedule?.LolBannedHours)}) — available again: {When(lol.AvailableAgainAt, now)}",
                        $"LoL PvP: tiltott időszak ({Range(s.Schedule?.LolBannedHours)}) — újra: {When(lol.AvailableAgainAt, now)}")
            : lol.Blocked
                ? Loc.T($"LoL PvP: used up ({lol.Used}/{lol.Limit}) — available again: {When(lol.AvailableAgainAt, now)}",
                        $"LoL PvP: elfogyott ({lol.Used}/{lol.Limit}) — újra: {When(lol.AvailableAgainAt, now)}")
                : Loc.T(
                    $"LoL PvP: {lol.Remaining} matches left (of {lol.Limit}{(lol.Bonus > 0 ? $", including +{lol.Bonus} rest-day bonus" : "")}{(lol.Extra > 0 ? $", including +{lol.Extra} extra from the admin" : "")})",
                    $"LoL PvP: {lol.Remaining} meccs maradt ({lol.Limit}-ból{(lol.Bonus > 0 ? $", ebből +{lol.Bonus} pihenőnap-jutalom" : "")}{(lol.Extra > 0 ? $", ebből +{lol.Extra} admin által adott extra" : "")})");

        var next = new List<string>();
        if (!exempt)
        {
            if (lol?.NextReleaseAt is { } lr) next.Add(Loc.T($"match {When(lr, now)}", $"meccs {When(lr, now)}"));
            if (s.ShortVideo?.NextReleaseAt is { } sr) next.Add(Loc.T($"short videos {When(sr, now)}", $"rövid videó {When(sr, now)}"));
            if (s.FacebookFeed?.NextReleaseAt is { } fr) next.Add(Loc.T($"feed {When(fr, now)}", $"hírfolyam {When(fr, now)}"));
        }

        return new StatusView(
            health,
            health == Health.Ok ? Loc.T("Protection: OK", "Védelem: rendben") : Loc.T("Warning: ", "Figyelmeztetés: ") + problems[0],
            lolText,
            Web(Loc.T("Short videos", "Rövid videók"), s.ShortVideo, s.Schedule?.ShortVideoBannedHours, now),
            Web(Loc.T("Facebook feed", "Facebook-hírfolyam"), s.FacebookFeed, s.Schedule?.FacebookFeedBannedHours, now),
            exempt ? Loc.T("No limit on weekends", "Hétvégén nincs korlát")
                : next.Count > 0 ? Loc.T("Next release: ", "Következő felszabadulás: ") + string.Join(", ", next)
                : Loc.T("All limits are free", "Minden keret szabad"),
            DescribeSchedule(s.Schedule),
            problems);
    }

    private static string Web(string label, WebLimitDto? w, BannedHoursDto? ban, DateTimeOffset now)
    {
        if (w is null) return $"{label}: –";
        if (w.Exempt) return Loc.T($"{label}: weekend — no limit", $"{label}: hétvége — nincs korlát");
        if (w.Banned) return Loc.T($"{label}: banned period ({Range(ban)}) — available again: {When(w.AvailableAgainAt, now)}",
                                   $"{label}: tiltott időszak ({Range(ban)}) — újra: {When(w.AvailableAgainAt, now)}");
        var limitMin = (int)Math.Round(w.LimitSeconds / 60);
        if (w.Blocked) return Loc.T($"{label}: used up — available again: {When(w.AvailableAgainAt, now)}",
                                    $"{label}: elfogyott — újra: {When(w.AvailableAgainAt, now)}");
        var bonusMin = (int)Math.Round(w.BonusSeconds / 60);
        var extraMin = (int)Math.Round(w.ExtraSeconds / 60);
        var detailEn = (bonusMin > 0 ? $", including +{bonusMin} min rest-day bonus" : "") +
                       (extraMin > 0 ? $", including +{extraMin} min extra from the admin" : "");
        var detailHu = (bonusMin > 0 ? $", ebből +{bonusMin} perc pihenőnap-jutalom" : "") +
                       (extraMin > 0 ? $", ebből +{extraMin} perc admin által adott extra" : "");
        return Loc.T($"{label}: {Minutes(w.RemainingSeconds)} left (of {limitMin} min{detailEn})",
                     $"{label}: {Minutes(w.RemainingSeconds)} maradt ({limitMin} percből{detailHu})");
    }

    /// <summary>The schedule chosen at install time, as text.</summary>
    public static string DescribeSchedule(ScheduleDto? schedule)
    {
        if (schedule is null) return "";
        var reset = schedule.ResetMode == IpcConstants.ResetModes.Daily
            ? Loc.T($"The limits reset daily at {schedule.DailyResetHour:00}:00.", $"A keretek naponta {schedule.DailyResetHour:00}:00-kor nullázódnak.")
            : Loc.T("Every limit covers the previous 24 hours; used time is released gradually 24 hours later.",
                    "Minden keret az előző 24 órára vonatkozik; a felhasznált idő 24 órával később fokozatosan felszabadul.");
        var weekend = schedule.EnforceOnWeekends
            ? Loc.T("The limits also apply on weekends.", "A korlátok hétvégén is érvényesek.")
            : Loc.T("There is no limit on weekends (Saturday, Sunday).", "Hétvégén (szombat, vasárnap) nincs korlát.");
        var bonusesEn = new List<string>();
        var bonusesHu = new List<string>();
        if (schedule.LolRestDayBonusMatches > 0)
        {
            bonusesEn.Add($"LoL +{schedule.LolRestDayBonusMatches} matches");
            bonusesHu.Add($"LoL +{schedule.LolRestDayBonusMatches} meccs");
        }
        if (schedule.ShortVideoRestDayBonusMinutes > 0)
        {
            bonusesEn.Add($"short videos +{schedule.ShortVideoRestDayBonusMinutes} min");
            bonusesHu.Add($"rövid videó +{schedule.ShortVideoRestDayBonusMinutes} perc");
        }
        if (schedule.FacebookFeedRestDayBonusMinutes > 0)
        {
            bonusesEn.Add($"feed +{schedule.FacebookFeedRestDayBonusMinutes} min");
            bonusesHu.Add($"hírfolyam +{schedule.FacebookFeedRestDayBonusMinutes} perc");
        }
        var bonus = bonusesEn.Count > 0
            ? Loc.T("\nRest-day bonus: if you use none of something on a day, you get more of it the next day (" + string.Join(", ", bonusesEn) + "); it does not stack.",
                    "\nPihenőnap-jutalom: ha egy napon valamiből semmit sem használsz, másnap ahhoz több jár (" + string.Join(", ", bonusesHu) + "); nem halmozódik.")
            : "";
        var bansEn = new List<string>();
        var bansHu = new List<string>();
        if (schedule.LolBannedHours is { Enabled: true } lb) { bansEn.Add($"LoL {Range(lb)}"); bansHu.Add($"LoL {Range(lb)}"); }
        if (schedule.ShortVideoBannedHours is { Enabled: true } sb) { bansEn.Add($"short videos {Range(sb)}"); bansHu.Add($"rövid videó {Range(sb)}"); }
        if (schedule.FacebookFeedBannedHours is { Enabled: true } fb) { bansEn.Add($"feed {Range(fb)}"); bansHu.Add($"hírfolyam {Range(fb)}"); }
        var banText = bansEn.Count > 0
            ? Loc.T("\nBanned hours (daily, on limited days): " + string.Join(", ", bansEn) + ".",
                    "\nTiltott órák (naponta, a korlátozott napokon): " + string.Join(", ", bansHu) + ".")
            : "";
        return reset + "\n" + weekend + bonus + banText;
    }

    /// <summary>"01:00–05:00".</summary>
    public static string Range(BannedHoursDto? ban) =>
        ban is null ? "–" : $"{ban.From:00}:00–{ban.To:00}:00";

    public static string Minutes(double seconds)
    {
        var total = (int)Math.Floor(seconds);
        return total >= 60 ? $"{total / 60} {Loc.T("min", "perc")}" : $"{total} {Loc.T("sec", "mp")}";
    }

    /// <summary>"today 14:32", "tomorrow 09:10" or "Oct 12 09:10" (Hungarian: "ma 14:32", "holnap 09:10", "okt. 12. 09:10").</summary>
    public static string When(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } value) return "–";
        var local = value.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var time = local.ToString("HH:mm", Loc.Culture);
        if (local.Date == today) return Loc.T("today ", "ma ") + time;
        if (local.Date == today.AddDays(1)) return Loc.T("tomorrow ", "holnap ") + time;
        return local.ToString(Loc.IsHungarian ? "MMM d. " : "MMM d, ", Loc.Culture) + time;
    }
}
