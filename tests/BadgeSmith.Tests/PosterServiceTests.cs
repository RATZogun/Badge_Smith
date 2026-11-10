using BadgeSmith.Desktop.Services;

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

    private string Cache(uint appId)
    {
        var folder = Path.Combine(_steam, "appcache", "librarycache", appId.ToString());
        Directory.CreateDirectory(folder);
        return folder;
    }
}
