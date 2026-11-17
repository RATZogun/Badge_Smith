using Avalonia.Media.Imaging;
using BadgeSmith.Core;
using BadgeSmith.Desktop.ViewModels;
using BadgeSmith.Steam;

namespace BadgeSmith.Desktop.Services;

public sealed class PosterService(StoreAssetsClient? client = null, string? steamRoot = null, AchievementIconClient? achievementIcons = null) : IDisposable
{
    private const int ParallelDownloads = 6;

    private readonly StoreAssetsClient _client = client ?? new StoreAssetsClient();
    private readonly AchievementIconClient _achievementIcons = achievementIcons ?? new AchievementIconClient();
    private readonly string? _steamRoot = steamRoot ?? SteamInstall.FindRoot();
    private readonly HashSet<uint> _loading = [];
    private readonly HashSet<uint> _withoutPoster = [];

    public async Task LoadAsync(IReadOnlyList<FarmGameViewModel> games)
    {
        List<FarmGameViewModel> claimed;
        lock (_loading)
        {
            claimed = games
                .Where(g => g.Poster is null && !_withoutPoster.Contains(g.AppId) && _loading.Add(g.AppId))
                .ToList();
        }

        try
        {
            await LoadClaimedAsync(claimed);
        }
        finally
        {
            lock (_loading)
            {
                _loading.ExceptWith(claimed.Select(g => g.AppId));
            }
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
            catch (Exception error) when (IsUnavailable(error))
            {
            }
        }
    }

    public async Task LoadAchievementIconsAsync(uint appId, IReadOnlyList<AchievementItemViewModel> items)
    {
        using var slots = new SemaphoreSlim(ParallelDownloads);
        await Task.WhenAll(items.Where(i => i.Icon is null && i.IconFile is not null).Select(async item =>
        {
            await slots.WaitAsync();
            try
            {
                var bytes = await _achievementIcons.DownloadAsync(appId, item.IconFile!, CancellationToken.None);
                item.Icon = TryOpen(() => new Bitmap(new MemoryStream(bytes)));
            }
            catch (Exception error) when (IsUnavailable(error))
            {
            }
            finally
            {
                slots.Release();
            }
        }));
    }

    public void Dispose()
    {
        _client.Dispose();
        _achievementIcons.Dispose();
    }

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

    private async Task LoadClaimedAsync(List<FarmGameViewModel> games)
    {
        var missing = new List<FarmGameViewModel>();
        foreach (var game in games)
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

        IReadOnlyDictionary<uint, string> urls;
        try
        {
            urls = await _client.GetPosterUrlsAsync(missing.Select(g => g.AppId), CancellationToken.None);
        }
        catch (Exception error) when (IsUnavailable(error))
        {
            return;
        }

        foreach (var game in missing)
        {
            if (!urls.TryGetValue(game.AppId, out var url))
            {
                lock (_loading)
                {
                    _withoutPoster.Add(game.AppId);
                }

                continue;
            }

            try
            {
                var bytes = await _client.DownloadAsync(url, CancellationToken.None);
                game.Poster = TryOpen(() => new Bitmap(new MemoryStream(bytes)));
            }
            catch (Exception error) when (IsUnavailable(error))
            {
            }
        }
    }

    private static bool IsUnavailable(Exception error) =>
        error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException;

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
