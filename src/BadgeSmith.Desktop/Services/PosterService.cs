using Avalonia.Media.Imaging;
using BadgeSmith.Core;
using BadgeSmith.Desktop.ViewModels;
using BadgeSmith.Steam;

namespace BadgeSmith.Desktop.Services;

public sealed class PosterService(StoreAssetsClient? client = null, string? steamRoot = null) : IDisposable
{
    private readonly StoreAssetsClient _client = client ?? new StoreAssetsClient();
    private readonly string? _steamRoot = steamRoot ?? SteamInstall.FindRoot();

    public async Task LoadAsync(IReadOnlyList<FarmGameViewModel> games)
    {
        var missing = new List<FarmGameViewModel>();
        foreach (var game in games.Where(g => g.Poster is null))
        {
            var local = _steamRoot is null ? null : FindLocalPoster(_steamRoot, game.AppId);
            game.Poster = local is null ? null : TryOpen(() => new Bitmap(local));
            if (game.Poster is null)
            {
                missing.Add(game);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        try
        {
            var urls = await _client.GetPosterUrlsAsync(missing.Select(g => g.AppId), CancellationToken.None);
            foreach (var game in missing)
            {
                if (urls.TryGetValue(game.AppId, out var url))
                {
                    var bytes = await _client.DownloadAsync(url, CancellationToken.None);
                    game.Poster = TryOpen(() => new Bitmap(new MemoryStream(bytes)));
                }
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
        }
    }

    public async Task LoadIconsAsync(IReadOnlyList<GameItemViewModel> games)
    {
        foreach (var game in games.Where(g => g.Icon is null && g.IconHash is not null))
        {
            var local = _steamRoot is null ? null : Path.Combine(_steamRoot, "appcache", "librarycache", game.AppId.ToString(), $"{game.IconHash}.jpg");
            game.Icon = local is not null && File.Exists(local) ? TryOpen(() => new Bitmap(local)) : null;
            if (game.Icon is not null)
            {
                continue;
            }

            try
            {
                var bytes = await _client.DownloadAsync(OwnedGamesClient.IconUrl(game.AppId, game.IconHash!), CancellationToken.None);
                game.Icon = TryOpen(() => new Bitmap(new MemoryStream(bytes)));
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
            }
        }
    }

    public void Dispose() => _client.Dispose();

    public static string? FindLocalPoster(string steamRoot, uint appId)
    {
        var folder = Path.Combine(steamRoot, "appcache", "librarycache", appId.ToString());
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var poster = Path.Combine(folder, "library_600x900.jpg");
        if (File.Exists(poster))
        {
            return poster;
        }

        return Directory.EnumerateFiles(folder, "library_capsule.jpg", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static Bitmap? TryOpen(Func<Bitmap> open)
    {
        try
        {
            return open();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
