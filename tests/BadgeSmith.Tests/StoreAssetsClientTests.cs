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

    [Fact]
    public void SkipsTheItemsInAnUnexpectedFormat()
    {
        const string json = """
            {"response":{"store_items":[
              {"appid":"x","assets":{"asset_url_format":"a/${FILENAME}","header":"h.jpg"}},
              {"appid":20,"assets":{"asset_url_format":7,"header":"h.jpg"}},
              {"appid":30,"assets":{"asset_url_format":"a/${FILENAME}","header":5}},
              {"appid":40,"assets":{"asset_url_format":"a/${FILENAME}","header":"h.jpg"}}]}}
            """;

        var posters = StoreAssetsClient.Parse(json);

        Assert.Equal([40u], posters.Keys);
    }
}
