using System.Net;
using BadgeSmith.Desktop.Services;
using BadgeSmith.Desktop.ViewModels;
using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public sealed class PosterServiceTests : IDisposable
{
    private readonly string _steam = Path.Combine(Path.GetTempPath(), $"badgesmith-steam-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_steam))
        {
            Directory.Delete(_steam, recursive: true);
        }
    }

    [Fact]
    public void PrefersTheClassicPosterInTheSteamCache()
    {
        var folder = Cache(10);
        File.WriteAllText(Path.Combine(folder, "library_600x900.jpg"), "");
        Directory.CreateDirectory(Path.Combine(folder, "abc"));
        File.WriteAllText(Path.Combine(folder, "abc", "library_capsule.jpg"), "");

        Assert.Equal(Path.Combine(folder, "library_600x900.jpg"), PosterService.FindLocalPoster(_steam, 10));
    }

    [Fact]
    public void FindsTheHashedPosterOfNewerGames()
    {
        var folder = Cache(20);
        Directory.CreateDirectory(Path.Combine(folder, "abc"));
        File.WriteAllText(Path.Combine(folder, "abc", "library_capsule.jpg"), "");
        File.WriteAllText(Path.Combine(folder, "header.jpg"), "");

        Assert.Equal(Path.Combine(folder, "abc", "library_capsule.jpg"), PosterService.FindLocalPoster(_steam, 20));
    }

    [Fact]
    public void ReturnsNothingWhenSteamHasNoPoster()
    {
        File.WriteAllText(Path.Combine(Cache(30), "header.jpg"), "");

        Assert.Null(PosterService.FindLocalPoster(_steam, 30));
        Assert.Null(PosterService.FindLocalPoster(_steam, 40));
    }

    [Fact]
    public async Task KeepsDownloadingTheOtherPostersWhenOneFails()
    {
        var store = new FakeStore(posters: [1, 2, 3], failing: [1]);
        using var service = new PosterService(new StoreAssetsClient(store), _steam);

        await service.LoadAsync([Game(1), Game(2), Game(3)]);

        Assert.Equal(["poster/1", "poster/2", "poster/3"], store.Downloads);
    }

    [Fact]
    public async Task DoesNotAskTheStoreAgainForGamesWithoutPoster()
    {
        var store = new FakeStore(posters: [1]);
        using var service = new PosterService(new StoreAssetsClient(store), _steam);

        await service.LoadAsync([Game(1), Game(2)]);
        await service.LoadAsync([Game(2)]);

        Assert.Equal(1, store.Lookups);
    }

    [Fact]
    public async Task DoesNotLoadTheSameGameTwiceAtOnce()
    {
        var store = new FakeStore(posters: [1]) { Gate = new TaskCompletionSource() };
        using var service = new PosterService(new StoreAssetsClient(store), _steam);
        var game = Game(1);

        var first = service.LoadAsync([game]);
        var second = service.LoadAsync([game]);
        store.Gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, store.Lookups);
        Assert.Equal(["poster/1"], store.Downloads);
    }

    private static FarmGameViewModel Game(uint appId) => new(appId, $"Jogo {appId}", false, _ => { });

    private string Cache(uint appId)
    {
        var folder = Path.Combine(_steam, "appcache", "librarycache", appId.ToString());
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class FakeStore(uint[] posters, uint[]? failing = null) : HttpMessageHandler
    {
        public TaskCompletionSource? Gate { get; init; }

        public int Lookups { get; private set; }

        public List<string> Downloads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "api.steampowered.com")
            {
                Lookups++;
                if (Gate is not null)
                {
                    await Gate.Task;
                }

                var items = posters.Select(id => $$$"""{"appid":{{{id}}},"assets":{"asset_url_format":"poster/${FILENAME}","header":"{{{id}}}"}}""");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$$"""{"response":{"store_items":[{{{string.Join(",", items)}}}]}}"""),
                };
            }

            var path = uri.AbsolutePath.Replace("/store_item_assets/", "", StringComparison.Ordinal);
            Downloads.Add(path);
            return failing?.Any(id => path == $"poster/{id}") == true
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        }
    }
}
