using Limiter.Core;
using Limiter.Protocol;

namespace Limiter.Service;

public sealed class StatusBuilder(LimitEngine engine, ProtectionState protection, SettingsProvider settings, IClock clock)
{
    public IpcResponse Build(string sid)
    {
        var lol = engine.GetLolStatus(sid);
        return new IpcResponse
        {
            Ok = true,
            ServerTime = clock.UtcNow,
            Lol = new LolLimitDto
            {
                Limit = lol.Limit,
                Used = lol.Used,
                Remaining = lol.Remaining,
                Blocked = lol.Blocked,
                NextReleaseAt = lol.NextReleaseAt,
                AvailableAgainAt = lol.AvailableAgainAt,
            },
            ShortVideo = Web(engine.GetWebStatus(sid, WebCategory.ShortVideo)),
            FacebookFeed = Web(engine.GetWebStatus(sid, WebCategory.FacebookFeed)),
            Protection = protection.Snapshot(sid, settings.Warning is { } w ? [w] : []),
        };
    }

    private static WebLimitDto Web(WebLimitStatus s) => new()
    {
        LimitSeconds = s.Limit.TotalSeconds,
        UsedSeconds = Math.Round(s.Used.TotalSeconds, 1),
        RemainingSeconds = Math.Round(s.Remaining.TotalSeconds, 1),
        Blocked = s.Blocked,
        NextReleaseAt = s.NextReleaseAt,
        AvailableAgainAt = s.AvailableAgainAt,
    };
}

/// <summary>
/// A kérések feldolgozása. Szándékosan nincs olyan parancs, amely számlálót csökkentene vagy nullázna:
/// a kliensek (bővítmény, tálcaalkalmazás) csak lekérdezni és használatot jelenteni tudnak.
/// </summary>
public sealed class RequestHandler(LimitEngine engine, StatusBuilder status, ProtectionState protection)
{
    public IpcResponse Handle(string sid, IpcRequest request)
    {
        double? accepted = null;
        switch (request.Type)
        {
            case IpcConstants.RequestTypes.Status:
                break;
            case IpcConstants.RequestTypes.Ping:
                if (request.Source == IpcConstants.Sources.Extension) protection.ExtensionPing(sid);
                break;
            case IpcConstants.RequestTypes.Usage:
                if (!WebCategoryNames.TryParse(request.Category, out var category))
                    return IpcResponse.Failure(request.Id, "Ismeretlen kategória.");
                if (request.Seconds is not { } seconds || !double.IsFinite(seconds))
                    return IpcResponse.Failure(request.Id, "Hiányzó időtartam.");
                accepted = engine.ReportWebUsage(sid, category, TimeSpan.FromSeconds(Math.Max(0, seconds))).TotalSeconds;
                if (request.Source == IpcConstants.Sources.Extension) protection.ExtensionPing(sid);
                break;
            default:
                return IpcResponse.Failure(request.Id, "Ismeretlen kéréstípus.");
        }

        var response = status.Build(sid);
        response.Id = request.Id;
        response.AcceptedSeconds = accepted;
        return response;
    }
}
