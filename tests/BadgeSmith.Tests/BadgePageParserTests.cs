using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class BadgePageParserTests
{
    private const string Html = """
        <html><body>
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
        </div>
        <div class="pageLinks"><a class="pagelink" href="?p=2">2</a><a class="pagelink" href="?p=3">3</a></div>
        </body></html>
        """;

    [Fact]
    public void ReadsGamesWithRemainingDrops()
    {
        var page = BadgePageParser.Parse(Html);

        Assert.True(page.IsSignedIn);
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
            });
    }

    [Fact]
    public void DetectsSignedOutPage()
    {
        var page = BadgePageParser.Parse("<html><body><div class=\"login_area\"></div></body></html>");

        Assert.False(page.IsSignedIn);
        Assert.Empty(page.Badges);
    }
}
