using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Limiter.Protocol;

public static class IpcConstants
{
    /// <summary>A szolgáltatás named pipe neve. A verziószám a protokoll megváltozásakor nő.</summary>
    public const string PipeName = "LolScrollLimiter.v1";

    /// <summary>A Chrome Native Messaging host neve (a registryben és a bővítményben ugyanez).</summary>
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

    public static class LcuStates
    {
        public const string NotRunning = "notRunning";
        public const string Connecting = "connecting";
        public const string Connected = "connected";
        public const string Degraded = "degraded";
        public const string Error = "error";
    }
}

/// <summary>Egy sorban (newline-delimited JSON) küldött kérés a szolgáltatás felé.</summary>
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
    /// <summary>A legrégebbi még számított meccs ekkor esik ki a 24 órás ablakból.</summary>
    public DateTimeOffset? NextReleaseAt { get; set; }
    /// <summary>Tiltott állapotban: ekkor lesz újra legalább egy szabad meccs.</summary>
    public DateTimeOffset? AvailableAgainAt { get; set; }
}

public sealed class WebLimitDto
{
    public double LimitSeconds { get; set; }
    public double UsedSeconds { get; set; }
    public double RemainingSeconds { get; set; }
    public bool Blocked { get; set; }
    public DateTimeOffset? NextReleaseAt { get; set; }
    public DateTimeOffset? AvailableAgainAt { get; set; }
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
