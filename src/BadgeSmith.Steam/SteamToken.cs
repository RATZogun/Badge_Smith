using System.Text.Json;

namespace BadgeSmith.Steam;

public sealed record SteamToken(ulong? SteamId, IReadOnlyList<string> Audiences, DateTimeOffset? ExpiresAt)
{
    public static SteamToken? Read(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            ulong? steamId = root.TryGetProperty("sub", out var sub) && ulong.TryParse(sub.GetString(), out var id) ? id : null;
            var audiences = root.TryGetProperty("aud", out var aud)
                ? aud.ValueKind == JsonValueKind.Array ? aud.EnumerateArray().Select(a => a.GetString() ?? "").ToList() : [aud.GetString() ?? ""]
                : new List<string>();
            DateTimeOffset? expiresAt = root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
            return new SteamToken(steamId, audiences, expiresAt);
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
