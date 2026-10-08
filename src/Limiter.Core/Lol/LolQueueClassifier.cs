namespace Limiter.Core.Lol;

/// <summary>A LoL-kliensből kiolvasott sor-/játékmód-adatok.</summary>
public sealed record QueueInfo(int QueueId, string? GameMode, string? Category, string? Type, bool IsCustom)
{
    public static QueueInfo Empty { get; } = new(0, null, null, null, false);

    public bool IsComplete => QueueId > 0 && Category is not null;

    /// <summary>A hiányzó mezőket a <paramref name="other"/> adataival egészíti ki.</summary>
    public QueueInfo FillFrom(QueueInfo other) => new(
        QueueId > 0 ? QueueId : other.QueueId,
        GameMode ?? other.GameMode,
        Category ?? other.Category,
        Type ?? other.Type,
        IsCustom || other.IsCustom);
}

public enum GameKind
{
    /// <summary>PvP LoL-meccs — fogyasztja a keretet.</summary>
    Pvp,

    /// <summary>TFT, botmeccs, gyakorlás, egyéni játék, oktatómód — nem fogyaszt.</summary>
    NotPvp,

    /// <summary>Nem állapítható meg: a program enged, és hibát jelez.</summary>
    Unknown,
}

public static class LolQueueClassifier
{
    // Co-op vs. AI sorok (régi és új azonosítók).
    private static readonly HashSet<int> BotQueues = [31, 32, 33, 52, 800, 810, 820, 830, 840, 850, 860, 870, 880, 890];

    // Oktató sorok.
    private static readonly HashSet<int> TutorialQueues = [2000, 2010, 2020];

    private static readonly HashSet<string> NonPvpGameModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TFT",
        "PRACTICETOOL",
        "STRAWBERRY", // Swarm (PvE)
    };

    public static GameKind Classify(QueueInfo queue)
    {
        if (queue.IsCustom) return GameKind.NotPvp;

        var mode = queue.GameMode ?? "";
        if (NonPvpGameModes.Contains(mode) || mode.StartsWith("TUTORIAL", StringComparison.OrdinalIgnoreCase))
            return GameKind.NotPvp;

        var type = queue.Type ?? "";
        if (type.Contains("TFT", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("BOT", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("TUTORIAL", StringComparison.OrdinalIgnoreCase))
            return GameKind.NotPvp;

        if (BotQueues.Contains(queue.QueueId) || TutorialQueues.Contains(queue.QueueId))
            return GameKind.NotPvp;

        if (string.Equals(queue.Category, "VersusAi", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(queue.Category, "Custom", StringComparison.OrdinalIgnoreCase))
            return GameKind.NotPvp;

        if (string.Equals(queue.Category, "PvP", StringComparison.OrdinalIgnoreCase))
            return GameKind.Pvp;

        // Kategória nélkül: valódi (pozitív) sorazonosító és ismert játékmód esetén PvP.
        if (queue.QueueId > 0 && !string.IsNullOrEmpty(queue.GameMode))
            return GameKind.Pvp;

        return GameKind.Unknown;
    }
}
