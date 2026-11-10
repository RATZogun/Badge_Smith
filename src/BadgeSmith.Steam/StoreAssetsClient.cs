using System.Text.Json;

namespace BadgeSmith.Steam;

public sealed class StoreAssetsClient(HttpMessageHandler? handler = null) : IDisposable
{
    private const string AssetHost = "https://shared.akamai.steamstatic.com/store_item_assets/";
    private const int BatchSize = 50;

    private readonly HttpClient _http = new(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyDictionary<uint, string>> GetPosterUrlsAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken)
    {
        var posters = new Dictionary<uint, string>();
        foreach (var batch in appIds.Distinct().Chunk(BatchSize))
        {
            var request = new
            {
                ids = batch.Select(id => new { appid = id }),
                context = new { language = "english", country_code = "US" },
                data_request = new { include_assets = true },
            };
            var url = $"https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json={Uri.EscapeDataString(JsonSerializer.Serialize(request))}";
            foreach (var (appId, poster) in Parse(await _http.GetStringAsync(url, cancellationToken)))
            {
                posters[appId] = poster;
            }
        }

        return posters;
    }

    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken) =>
        await _http.GetByteArrayAsync(url, cancellationToken);

    public void Dispose() => _http.Dispose();

    public static IReadOnlyDictionary<uint, string> Parse(string json)
    {
        var posters = new Dictionary<uint, string>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("response", out var response)
            || !response.TryGetProperty("store_items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return posters;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("appid", out var appId)
                || !item.TryGetProperty("assets", out var assets)
                || !assets.TryGetProperty("asset_url_format", out var format))
            {
                continue;
            }

            var file = Asset(assets, "library_capsule") ?? Asset(assets, "header");
            if (file is not null && format.GetString() is { } template)
            {
                posters[appId.GetUInt32()] = AssetHost + template.Replace("${FILENAME}", file, StringComparison.Ordinal);
            }
        }

        return posters;
    }

    private static string? Asset(JsonElement assets, string name) =>
        assets.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } file ? file : null;
}
