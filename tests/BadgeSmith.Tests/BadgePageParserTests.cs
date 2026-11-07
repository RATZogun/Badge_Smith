using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class BadgePageParserTests
{
    private const string Html = """
        <html><head><script>g_steamID = "76561198000000001";</script></head><body>
        <div class="profile_xp_block"></div>
        <div class="badges_sheet">
          <div class="badge_row is_link">
            <a class="badge_row_overlay" href="https://steamcommunity.com/id/x/gamecards/4983490/"></a>
            <div class="badge_title_row">
              <div class="badge_title_stats">
                <div class="badge_title_stats_playtime">1,234.5 hrs on record</div>
                <div class="badge_title_stats_drops"><span class="progress_info_bold">3 card drops remaining</span></div>
              </div>
              <div class="badge_title">Last Gambit&nbsp;<span class="badge_view_details">View details</span></div>
            </div>
          </div>
          <div class="badge_row is_link">
            <a class="badge_row_overlay" href="https://steamcommunity.com/id/x/gamecards/5257350/"></a>
            <div class="badge_title_stats_drops"><span class="progress_info_bold">1 card drop remaining</span></div>
            <div class="badge_title">Hex Reverse</div>
          </div>
          <div class="badge_row is_link">
            <a class="badge_row_overlay" href="https://steamcommunity.com/id/x/gamecards/105600/?border=1"></a>
            <div class="badge_title_stats_drops"><span class="progress_info_bold">2 card drops remaining</span></div>
            <div class="badge_title">Terraria (Foil)</div>
          </div>
          <div class="badge_row is_link">
            <a class="badge_row_overlay" href="https://steamcommunity.com/id/x/gamecards/1055540/"></a>
            <div class="badge_title_stats_drops"><span class="progress_info_bold">No card drops remaining</span></div>
            <div class="badge_title">A Short Hike</div>
          </div>
          <div class="badge_row is_link">
            <a class="badge_row_overlay" href="https://steamcommunity.com/id/x/gamecards/979070/"></a>
            <div class="badge_title_stats_playtime">0.3 hrs on record</div>
            <div class="badge_title">Just Die Already</div>
          </div>
        </div>
        <div class="pageLinks"><a class="pagelink" href="?p=2">2</a><a class="pagelink" href="?p=3">3</a></div>
        </body></html>
        """;

    [Fact]
    public void ReadsGamesAndTheirRemainingDrops()
    {
        var page = BadgePageParser.Parse(Html);

        Assert.Equal(76561198000000001ul, page.SignedInSteamId);
        Assert.Equal(3, page.PageCount);
        Assert.Collection(
            page.Badges,
            b =>
            {
                Assert.Equal(4983490u, b.AppId);
                Assert.Equal("Last Gambit", b.Name);
                Assert.Equal(3, b.RemainingDrops);
                Assert.Equal(1234.5, b.HoursPlayed);
            },
            b =>
            {
                Assert.Equal(5257350u, b.AppId);
                Assert.Equal(1, b.RemainingDrops);
                Assert.Equal(0, b.HoursPlayed);
            },
            b =>
            {
                Assert.Equal(1055540u, b.AppId);
                Assert.Equal(0, b.RemainingDrops);
            });
    }

    [Fact]
    public void ListsPlayedGamesWithoutDropInfo()
    {
        var page = BadgePageParser.Parse(Html);

        var badge = Assert.Single(page.WithoutDropInfo);
        Assert.Equal(979070u, badge.AppId);
        Assert.Equal(0.3, badge.HoursPlayed);
    }

    [Fact]
    public void TreatsPublicPageWithoutSessionAsSignedOut()
    {
        var page = BadgePageParser.Parse("""
            <html><head><script>g_steamID = false;</script></head><body>
            <div class="profile_xp_block"></div><div class="badges_sheet"></div>
            </body></html>
            """);

        Assert.Null(page.SignedInSteamId);
        Assert.Empty(page.Badges);
    }

    [Theory]
    [InlineData("You can get 4 more trading cards. 4 card drops remaining", 4)]
    [InlineData("1 card drop remaining", 1)]
    [InlineData("No card drops remaining", 0)]
    [InlineData("nothing here", null)]
    public void ReadsRemainingDropsFromText(string text, int? expected)
    {
        Assert.Equal(expected, BadgePageParser.ParseRemainingDrops(text));
    }
}
