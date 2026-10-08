using System.Text.Json;
using Limiter.Core.Lol;

namespace Limiter.Core.Tests;

public class LcuJsonTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string SessionJson = """
        {
          "phase": "InProgress",
          "gameData": {
            "gameId": 7123456789,
            "isCustomGame": false,
            "queue": { "id": 420, "gameMode": "CLASSIC", "category": "PvP", "type": "RANKED_SOLO_5x5" },
            "teamOne": [ { "puuid": "me-puuid", "summonerId": 11 }, { "puuid": "other", "summonerId": 12 } ],
            "teamTwo": [ { "puuid": "enemy", "summonerId": 13 } ]
          }
        }
        """;

    [Fact]
    public void Parses_session_and_detects_participant()
    {
        var snap = LcuJson.ParseSession(J(SessionJson), new LocalPlayer("me-puuid", 11));
        Assert.Equal("InProgress", snap.Phase);
        Assert.Equal(7123456789, snap.GameId);
        Assert.Equal(420, snap.Queue.QueueId);
        Assert.Equal(GameKind.Pvp, LolQueueClassifier.Classify(snap.Queue));
        Assert.True(snap.LocalPlayerIsParticipant);
    }

    [Fact]
    public void Spectator_is_not_a_participant()
    {
        var snap = LcuJson.ParseSession(J(SessionJson), new LocalPlayer("spectator-puuid", 99));
        Assert.False(snap.LocalPlayerIsParticipant);
    }

    [Fact]
    public void Missing_teams_mean_unknown_participation()
    {
        var snap = LcuJson.ParseSession(J("""{ "phase": "InProgress", "gameData": { "gameId": 5, "queue": { "id": 450 } } }"""), new LocalPlayer("x", 1));
        Assert.Null(snap.LocalPlayerIsParticipant);
    }

    [Fact]
    public void Custom_game_flag_is_propagated()
    {
        var snap = LcuJson.ParseSession(J("""{ "phase": "InProgress", "gameData": { "gameId": 5, "isCustomGame": true, "queue": { "id": -1, "gameMode": "CLASSIC" } } }"""), null);
        Assert.Equal(GameKind.NotPvp, LolQueueClassifier.Classify(snap.Queue));
    }

    [Fact]
    public void Lobby_queue_is_parsed_and_completed_from_queue_details()
    {
        var lobby = LcuJson.ParseLobbyQueue(J("""{ "gameConfig": { "queueId": 1090, "gameMode": "TFT", "isCustom": false } }"""));
        var details = LcuJson.ParseQueue(J("""{ "id": 1090, "gameMode": "TFT", "category": "PvP", "type": "NORMAL_TFT" }"""));
        var merged = lobby.FillFrom(details);
        Assert.Equal("NORMAL_TFT", merged.Type);
        Assert.Equal(GameKind.NotPvp, LolQueueClassifier.Classify(merged));
    }

    [Fact]
    public void Remake_requires_the_early_surrender_flag()
    {
        var remake = J("""{ "gameId": 1, "gameDuration": 200, "participants": [ { "stats": { "gameEndedInEarlySurrender": false } }, { "stats": { "gameEndedInEarlySurrender": true } } ] }""");
        var shortButReal = J("""{ "gameId": 2, "gameDuration": 190, "participants": [ { "stats": { "gameEndedInEarlySurrender": false } } ] }""");
        var noInfo = J("""{ "gameId": 3, "gameDuration": 150 }""");

        Assert.True(LcuJson.ParseEarlySurrender(remake));
        Assert.False(LcuJson.ParseEarlySurrender(shortButReal));
        Assert.Null(LcuJson.ParseEarlySurrender(noInfo));
    }

    [Theory]
    [InlineData("\"C:/Riot Games/League of Legends/LeagueClientUx.exe\" \"--riotclient-auth-token=abc\" \"--app-port=51234\" \"--remoting-auth-token=Xy_Z-09ab\"", 51234, "Xy_Z-09ab")]
    [InlineData("LeagueClientUx.exe --remoting-auth-token=tok --app-port=1", 1, "tok")]
    public void Parses_client_command_line(string cmd, int port, string token)
    {
        Assert.True(LcuJson.TryParseCommandLine(cmd, out var p, out var t));
        Assert.Equal(port, p);
        Assert.Equal(token, t);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("LeagueClientUx.exe --app-port=123")]
    [InlineData("LeagueClientUx.exe --app-port=99999999 --remoting-auth-token=a")]
    public void Rejects_incomplete_command_line(string? cmd) =>
        Assert.False(LcuJson.TryParseCommandLine(cmd, out _, out _));
}

public class ClassifierTests
{
    [Theory]
    [InlineData(420, "CLASSIC", "PvP", "RANKED_SOLO_5x5", false, GameKind.Pvp)]
    [InlineData(440, "CLASSIC", "PvP", "RANKED_FLEX_SR", false, GameKind.Pvp)]
    [InlineData(400, "CLASSIC", "PvP", "NORMAL", false, GameKind.Pvp)]
    [InlineData(450, "ARAM", "PvP", "ARAM_UNRANKED_5x5", false, GameKind.Pvp)]
    [InlineData(1700, "CHERRY", "PvP", "CHERRY", false, GameKind.Pvp)]
    [InlineData(450, "ARAM", null, null, false, GameKind.Pvp)]
    [InlineData(1100, "TFT", "PvP", "RANKED_TFT", false, GameKind.NotPvp)]
    [InlineData(1160, null, "PvP", "RANKED_TFT_DOUBLE_UP", false, GameKind.NotPvp)]
    [InlineData(870, "CLASSIC", "VersusAi", "BOT", false, GameKind.NotPvp)]
    [InlineData(890, "CLASSIC", null, null, false, GameKind.NotPvp)]
    [InlineData(0, "PRACTICETOOL", null, null, true, GameKind.NotPvp)]
    [InlineData(-1, "CLASSIC", null, null, true, GameKind.NotPvp)]
    [InlineData(1810, "STRAWBERRY", "VersusAi", "STRAWBERRY", false, GameKind.NotPvp)]
    [InlineData(0, null, null, null, false, GameKind.Unknown)]
    public void Classifies_queues(int id, string? mode, string? category, string? type, bool custom, GameKind expected) =>
        Assert.Equal(expected, LolQueueClassifier.Classify(new QueueInfo(id, mode, category, type, custom)));
}
