using System.Net;

namespace BadgeSmith.Steam;

public sealed class SteamWebSession
{
    public ulong SteamId { get; }
    public string AccessToken { get; }
    public CookieContainer Cookies { get; } = new();

    public bool IsForCommunity => SteamToken.Read(AccessToken)?.Audiences.Contains("web:community") ?? false;

    public string? Problem(DateTimeOffset now) => SteamToken.Read(AccessToken) switch
    {
        null => "O cookie steamLoginSecure não está no formato esperado.",
        { ExpiresAt: { } expires } when expires <= now => "Esse cookie já expirou. Copie um novo.",
        _ => null,
    };

    public static bool IsCookie(string value) =>
        value.Contains("%7C%7C", StringComparison.OrdinalIgnoreCase) || value.Contains("||", StringComparison.Ordinal);

    public SteamWebSession(string steamLoginSecure)
    {
        var value = steamLoginSecure.Trim();
        var decoded = WebUtility.UrlDecode(value);
        var separator = decoded.IndexOf("||", StringComparison.Ordinal);
        if (separator <= 0 || !ulong.TryParse(decoded[..separator], out var steamId))
        {
            throw new ArgumentException("O cookie steamLoginSecure não está no formato esperado (<steamid>||<token>).");
        }

        SteamId = steamId;
        AccessToken = decoded[(separator + 2)..];
        var domain = new Uri("https://steamcommunity.com");
        Cookies.Add(domain, new Cookie("steamLoginSecure", value) { Secure = true, HttpOnly = true });
        Cookies.Add(domain, new Cookie("Steam_Language", "english"));
    }
}
