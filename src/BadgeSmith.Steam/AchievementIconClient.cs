namespace BadgeSmith.Steam;

public sealed class AchievementIconClient(HttpMessageHandler? handler = null) : IDisposable
{
    private readonly HttpClient _http = new(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<byte[]> DownloadAsync(uint appId, string file, CancellationToken cancellationToken) =>
        await _http.GetByteArrayAsync(IconUrl(appId, file), cancellationToken);

    public void Dispose() => _http.Dispose();

    public static string IconUrl(uint appId, string file) =>
        $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{file}";
}
