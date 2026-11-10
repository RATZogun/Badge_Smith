using System.Text.Json;

namespace BadgeSmith.Steam;

public sealed record OwnedGame(uint AppId, string Name, double HoursPlayed, bool HasAchievements, string? IconHash = null);

public sealed record AchievementProgress(int Unlocked, int Total);

public sealed class OwnedGamesClient(HttpMessageHandler? handler = null) : IDisposable
{
    private const int ProgressBatchSize = 50;

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

    public async Task<IReadOnlyDictionary<uint, AchievementProgress>> GetAchievementProgressAsync(
        SteamWebSession session,
        IEnumerable<uint> appIds,
        CancellationToken cancellationToken)
    {
        var progress = new Dictionary<uint, AchievementProgress>();
        foreach (var batch in appIds.Distinct().Chunk(ProgressBatchSize))
        {
            var input = $$"""{"steamid":"{{session.SteamId}}","language":"english","appids":[{{string.Join(",", batch)}}]}""";
            var url = "https://api.steampowered.com/IPlayerService/GetAchievementsProgress/v1/"
                + $"?access_token={Uri.EscapeDataString(session.AccessToken)}&input_json={Uri.EscapeDataString(input)}";
            using var response = await _http.PostAsync(url, new StringContent(""), cancellationToken);
            response.EnsureSuccessStatusCode();
            foreach (var (appId, item) in ParseProgress(await response.Content.ReadAsStringAsync(cancellationToken)))
            {
                progress[appId] = item;
            }
        }

        return progress;
    }

    public void Dispose() => _http.Dispose();

    public static string IconUrl(uint appId, string iconHash) =>
        $"https://media.steampowered.com/steamcommunity/public/images/apps/{appId}/{iconHash}.jpg";

    public static IReadOnlyDictionary<uint, AchievementProgress> ParseProgress(string json)
    {
        var progress = new Dictionary<uint, AchievementProgress>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("response", out var response)
            || !response.TryGetProperty("achievement_progress", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return progress;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("appid", out var appId)
                && item.TryGetProperty("total", out var total)
                && total.GetInt32() > 0)
            {
                var unlocked = item.TryGetProperty("unlocked", out var value) ? value.GetInt32() : 0;
                progress[appId.GetUInt32()] = new AchievementProgress(unlocked, total.GetInt32());
            }
        }

        return progress;
    }

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
                g.TryGetProperty("has_community_visible_stats", out var stats) && stats.ValueKind == JsonValueKind.True,
                g.TryGetProperty("img_icon_url", out var icon) && icon.GetString() is { Length: > 0 } hash ? hash : null))
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
