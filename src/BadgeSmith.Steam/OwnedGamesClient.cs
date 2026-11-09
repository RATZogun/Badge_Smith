using System.Text.Json;

namespace BadgeSmith.Steam;

public sealed record OwnedGame(uint AppId, string Name, double HoursPlayed, bool HasAchievements);

public sealed class OwnedGamesClient(HttpMessageHandler? handler = null) : IDisposable
{
    private readonly HttpClient _http = new(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<OwnedGame>> GetAsync(SteamWebSession session, CancellationToken cancellationToken)
    {
        var url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
            + $"?access_token={Uri.EscapeDataString(session.AccessToken)}&steamid={session.SteamId}"
            + "&include_appinfo=true&include_played_free_games=true";
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw new SteamSessionExpiredException();
        }

        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public void Dispose() => _http.Dispose();

    public static IReadOnlyList<OwnedGame> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("response", out var response)
            || !response.TryGetProperty("games", out var games)
            || games.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return games.EnumerateArray()
            .Select(g => new OwnedGame(
                g.GetProperty("appid").GetUInt32(),
                g.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                g.TryGetProperty("playtime_forever", out var minutes) ? minutes.GetInt32() / 60.0 : 0,
                g.TryGetProperty("has_community_visible_stats", out var stats) && stats.ValueKind == JsonValueKind.True))
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
