using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class OwnedGamesClientTests
{
    [Fact]
    public void ReadsTheOwnedGamesSortedByName()
    {
        const string json = """
            {"response":{"game_count":3,"games":[
              {"appid":620,"name":"Portal 2","playtime_forever":90,"has_community_visible_stats":true,"img_icon_url":"abc123"},
              {"appid":10,"name":"Counter-Strike","playtime_forever":0},
              {"appid":400,"name":"Portal","playtime_forever":30,"has_community_visible_stats":false}]}}
            """;

        var games = OwnedGamesClient.Parse(json);

        Assert.Equal(["Counter-Strike", "Portal", "Portal 2"], games.Select(g => g.Name));
        Assert.Equal(new OwnedGame(620, "Portal 2", 1.5, true, "abc123"), games[2]);
        Assert.Equal("https://media.steampowered.com/steamcommunity/public/images/apps/620/abc123.jpg", OwnedGamesClient.IconUrl(620, "abc123"));
        Assert.False(games[0].HasAchievements);
    }

    [Fact]
    public void ReturnsNothingForAPrivateLibrary()
    {
        Assert.Empty(OwnedGamesClient.Parse("""{"response":{}}"""));
    }

    [Fact]
    public void ReadsTheAchievementProgressOfEachGame()
    {
        const string json = """
            {"response":{"achievement_progress":[
              {"appid":625960,"unlocked":6,"total":74,"percentage":8.1},
              {"appid":1245620,"unlocked":42,"total":42},
              {"appid":10,"unlocked":0,"total":0}]}}
            """;

        var progress = OwnedGamesClient.ParseProgress(json);

        Assert.Equal(new AchievementProgress(6, 74), progress[625960]);
        Assert.Equal(new AchievementProgress(42, 42), progress[1245620]);
        Assert.False(progress.ContainsKey(10));
    }
}
