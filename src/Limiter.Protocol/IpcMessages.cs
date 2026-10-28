using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Limiter.Protocol;

public static class IpcConstants
{
    /// <summary>The name of the service's named pipe. The version number grows when the protocol changes.</summary>
    public const string PipeName = "LolScrollLimiter.v1";

    /// <summary>The name of the Chrome Native Messaging host (the same in the registry and in the extension).</summary>
    public const string NativeHostName = "hu.kmsoft.lolscrolllimiter";

    public const string ServiceName = "LolScrollLimiter";

    public static class RequestTypes
    {
        public const string Status = "status";
        public const string Ping = "ping";
        public const string Usage = "usage";
    }

    public static class Categories
    {
        public const string ShortVideo = "shortVideo";
        public const string FacebookFeed = "facebookFeed";
    }

    public static class Sources
    {
        public const string Extension = "extension";
        public const string Tray = "tray";
    }

    public static class ResetModes
    {
        public const string Rolling = "rolling";
        public const string Daily = "daily";
    }

    public static class LcuStates
    {
        public const string NotRunning = "notRunning";
        public const string Connecting = "connecting";
        public const string Connected = "connected";
        public const string Degraded = "degraded";
        public const string Error = "error";
    }
}

/// <summary>A request to the service sent on one line (newline-delimited JSON).</summary>
public sealed class IpcRequest
{
    public string? Id { get; set; }
    public string Type { get; set; } = IpcConstants.RequestTypes.Status;
    public string? Category { get; set; }
    public double? Seconds { get; set; }
    public string? Source { get; set; }
}

public sealed class IpcResponse
{
    public string? Id { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset ServerTime { get; set; }
    public double? AcceptedSeconds { get; set; }
    public LolLimitDto? Lol { get; set; }
    public WebLimitDto? ShortVideo { get; set; }
    public WebLimitDto? FacebookFeed { get; set; }
    public ScheduleDto? Schedule { get; set; }
    public ProtectionDto? Protection { get; set; }

    public static IpcResponse Failure(string? id, string error) =>
        new() { Id = id, Ok = false, Error = error, ServerTime = DateTimeOffset.UtcNow };
}

public sealed class LolLimitDto
{
    public int Limit { get; set; }
    public int Used { get; set; }
    public int Remaining { get; set; }
    public bool Blocked { get; set; }
    /// <summary>The oldest match still counted drops out of the 24-hour window at this time.</summary>
    public DateTimeOffset? NextReleaseAt { get; set; }
    /// <summary>When blocked: at this time at least one match is free again.</summary>
    public DateTimeOffset? AvailableAgainAt { get; set; }
    /// <summary>Exempt period (weekend): does not block, does not use up.</summary>
    public bool Exempt { get; set; }
    /// <summary>Rest-day bonus (already included in <see cref="Limit"/>).</summary>
    public int Bonus { get; set; }
    /// <summary>Extra matches given by the admin for today (already included in <see cref="Limit"/>).</summary>
    public int Extra { get; set; }
    /// <summary>A banned period is active (then <see cref="Blocked"/> is true too).</summary>
    public bool Banned { get; set; }
}

public sealed class WebLimitDto
{
    public double LimitSeconds { get; set; }
    public double UsedSeconds { get; set; }
    public double RemainingSeconds { get; set; }
    public bool Blocked { get; set; }
    public DateTimeOffset? NextReleaseAt { get; set; }
    public DateTimeOffset? AvailableAgainAt { get; set; }
    /// <summary>Exempt period (weekend): does not block, does not use up.</summary>
    public bool Exempt { get; set; }
    /// <summary>Rest-day bonus in seconds (already included in <see cref="LimitSeconds"/>).</summary>
    public double BonusSeconds { get; set; }
    /// <summary>Extra given by the admin for today, in seconds (already included in <see cref="LimitSeconds"/>).</summary>
    public double ExtraSeconds { get; set; }
    /// <summary>A banned period is active (then <see cref="Blocked"/> is true too).</summary>
    public bool Banned { get; set; }
}

/// <summary>The schedule chosen at install time.</summary>
public sealed class ScheduleDto
{
    /// <summary>"rolling" (rolling 24 hours) or "daily" (daily reset).</summary>
    public string ResetMode { get; set; } = IpcConstants.ResetModes.Rolling;
    public int DailyResetHour { get; set; }
    public bool EnforceOnWeekends { get; set; }
    /// <summary>Whether an exempt period is active right now.</summary>
    public bool ExemptNow { get; set; }
    /// <summary>Rest-day bonus: this much is given for the next day if nothing was used on a day.</summary>
    public int LolRestDayBonusMatches { get; set; }
    public int ShortVideoRestDayBonusMinutes { get; set; }
    public int FacebookFeedRestDayBonusMinutes { get; set; }
    /// <summary>Banned hours per limit.</summary>
    public BannedHoursDto? LolBannedHours { get; set; }
    public BannedHoursDto? ShortVideoBannedHours { get; set; }
    public BannedHoursDto? FacebookFeedBannedHours { get; set; }
}

/// <summary>A banned period: from hour From (inclusive) to hour To (exclusive), local time.</summary>
public sealed class BannedHoursDto
{
    public bool Enabled { get; set; }
    public int From { get; set; }
    public int To { get; set; }
}

public sealed class ProtectionDto
{
    public string LcuState { get; set; } = IpcConstants.LcuStates.NotRunning;
    public string? LcuMessage { get; set; }
    public DateTimeOffset? LastExtensionPingAt { get; set; }
    public string? LastLolAction { get; set; }
    public DateTimeOffset? LastLolActionAt { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
