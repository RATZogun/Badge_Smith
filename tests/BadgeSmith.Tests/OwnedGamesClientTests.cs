using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class OwnedGamesClientTests
{
    [Fact]
    public void ReadsTheOwnedGamesSortedByName()
    {
        const string json = """
            {"response":{"game_count":3,"games":[
              {"appid":620,"name":"Portal 2","playtime_forever":90,"has_community_visible_stats":true},
              {"appid":10,"name":"Counter-Strike","playtime_forever":0},
              {"appid":400,"name":"Portal","playtime_forever":30,"has_community_visible_stats":false}]}}
            """;

        var games = OwnedGamesClient.Parse(json);

        Assert.Equal(["Counter-Strike", "Portal", "Portal 2"], games.Select(g => g.Name));
        Assert.Equal(new OwnedGame(620, "Portal 2", 1.5, true), games[2]);
        Assert.False(games[0].HasAchievements);
    }

    [Fact]
    public void ReturnsNothingForAPrivateLibrary()
    {
        Assert.Empty(OwnedGamesClient.Parse("""{"response":{}}"""));
    }
}
