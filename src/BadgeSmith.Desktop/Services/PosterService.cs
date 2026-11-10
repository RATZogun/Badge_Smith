using Avalonia.Media.Imaging;
using BadgeSmith.Desktop.ViewModels;
using BadgeSmith.Steam;

namespace BadgeSmith.Desktop.Services;

public sealed class PosterService(StoreAssetsClient? client = null, string? folder = null) : IDisposable
{
    private readonly StoreAssetsClient _client = client ?? new StoreAssetsClient();
    private readonly string _folder = folder ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "BadgeSmith",
        "posters");

    public async Task LoadAsync(IReadOnlyList<FarmGameViewModel> games)
    {
        var missing = new List<FarmGameViewModel>();
        foreach (var game in games.Where(g => g.Poster is null))
        {
            var path = PathFor(game.AppId);
            if (File.Exists(path) && TryOpen(path) is { } cached)
            {
                game.Poster = cached;
            }
            else
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
            Directory.CreateDirectory(_folder);
            foreach (var game in missing)
            {
                if (!urls.TryGetValue(game.AppId, out var url))
                {
                    continue;
                }

                var path = PathFor(game.AppId);
                await File.WriteAllBytesAsync(path, await _client.DownloadAsync(url, CancellationToken.None));
                game.Poster = TryOpen(path);
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException)
        {
        }
    }

    public void Dispose() => _client.Dispose();

    private string PathFor(uint appId) => Path.Combine(_folder, $"{appId}.jpg");

    private static Bitmap? TryOpen(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception)
        {
            File.Delete(path);
            return null;
        }
    }
}
