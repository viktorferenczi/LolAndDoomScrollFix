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
            : lol.Blocked
                ? $"LoL PvP: elfogyott ({lol.Used}/{lol.Limit}) — újra: {When(lol.AvailableAgainAt, now)}"
                : $"LoL PvP: {lol.Remaining} meccs maradt ({lol.Limit}-ból)";

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
            Web("Rövid videók", s.ShortVideo, now),
            Web("Facebook-hírfolyam", s.FacebookFeed, now),
            exempt ? "Hétvégén nincs korlát"
                : next.Count > 0 ? "Következő felszabadulás: " + string.Join(", ", next) : "Minden keret szabad",
            DescribeSchedule(s.Schedule),
            problems);
    }

    private static string Web(string label, WebLimitDto? w, DateTimeOffset now)
    {
        if (w is null) return $"{label}: –";
        if (w.Exempt) return $"{label}: hétvége — nincs korlát";
        var limitMin = (int)Math.Round(w.LimitSeconds / 60);
        if (w.Blocked) return $"{label}: elfogyott — újra: {When(w.AvailableAgainAt, now)}";
        return $"{label}: {Minutes(w.RemainingSeconds)} maradt ({limitMin} percből)";
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
        return reset + "\n" + weekend;
    }

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
