using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class StoreAssetsClientTests
{
    [Fact]
    public void BuildsThePosterUrlFromTheStoreAssets()
    {
        const string json = """
            {"response":{"store_items":[
              {"appid":625960,"assets":{"asset_url_format":"steam/apps/625960/${FILENAME}?t=1","library_capsule":"library_600x900.jpg","header":"h/header.jpg"}},
              {"appid":3981100,"assets":{"asset_url_format":"steam/apps/3981100/${FILENAME}?t=2","header":"abc/header.jpg"}},
              {"appid":10}]}}
            """;

        var posters = StoreAssetsClient.Parse(json);

        Assert.Equal(2, posters.Count);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/625960/library_600x900.jpg?t=1", posters[625960]);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3981100/abc/header.jpg?t=2", posters[3981100]);
    }
}
