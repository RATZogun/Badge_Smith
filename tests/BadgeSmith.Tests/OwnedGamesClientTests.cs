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

    [Fact]
    public void SkipsTheProgressInAnUnexpectedFormat()
    {
        const string json = """
            {"response":{"achievement_progress":[
              {"appid":"x","unlocked":1,"total":2},
              {"appid":20,"unlocked":1,"total":"2"},
              {"appid":30,"unlocked":null,"total":4},
              {"appid":40,"unlocked":3,"total":5}]}}
            """;

        var progress = OwnedGamesClient.ParseProgress(json);

        Assert.Equal(2, progress.Count);
        Assert.Equal(new AchievementProgress(0, 4), progress[30]);
        Assert.Equal(new AchievementProgress(3, 5), progress[40]);
    }

    [Fact]
    public async Task SendsTheAccessTokenInTheBodyInsteadOfTheUrl()
    {
        var web = new FakeWeb();
        using var client = new OwnedGamesClient(web);

        var progress = await client.GetAchievementProgressAsync(
            new SteamWebSession("76561198000000001%7C%7Csecret.token.value"),
            [10u, 20u],
            CancellationToken.None);

        Assert.Equal(new AchievementProgress(1, 2), progress[10]);
        var request = Assert.Single(web.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.DoesNotContain("secret.token.value", request.Url, StringComparison.Ordinal);
        Assert.Contains("access_token=secret.token.value", request.Body, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("\"appids\":[10,20]"), request.Body, StringComparison.Ordinal);
    }

    private sealed record SentRequest(HttpMethod Method, string Url, string Body);

    private sealed class FakeWeb : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method, request.RequestUri!.ToString(), body));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"response":{"achievement_progress":[{"appid":10,"unlocked":1,"total":2}]}}"""),
            };
        }
    }
}
