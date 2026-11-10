using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class AchievementIconClientTests
{
    [Fact]
    public void BuildsTheIconUrlFromTheSchemaFile()
    {
        Assert.Equal(
            "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/10/a.jpg",
            AchievementIconClient.IconUrl(10, "a.jpg"));
    }
}
