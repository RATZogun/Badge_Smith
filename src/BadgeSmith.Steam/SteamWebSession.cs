using System.Net;

namespace BadgeSmith.Steam;

public sealed class SteamWebSession
{
    public ulong SteamId { get; }
    public CookieContainer Cookies { get; } = new();

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
        var domain = new Uri("https://steamcommunity.com");
        Cookies.Add(domain, new Cookie("steamLoginSecure", value) { Secure = true, HttpOnly = true });
        Cookies.Add(domain, new Cookie("Steam_Language", "english"));
    }
}
