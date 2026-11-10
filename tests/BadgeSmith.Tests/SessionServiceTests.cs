using System.Net;
using System.Text;
using BadgeSmith.Desktop.Services;
using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class SessionServiceTests
{
    private const ulong SteamId = 76561198000000001;

    [Fact]
    public async Task SavesAPastedCookieAfterCheckingIt()
    {
        var web = new FakeWeb();
        var vault = new MemoryVault();
        using var session = NewSession(vault, web);
        var cookie = Cookie("web:store", 2000000000);

        var opened = await session.SaveCookieAsync($" steamLoginSecure={cookie} ", CancellationToken.None);

        Assert.Equal(cookie, vault.Value);
        Assert.Equal((SteamId, SteamId), (opened.SteamId, session.SteamId));
        Assert.False(session.CanReadBadges);
        Assert.Equal(["api.steampowered.com"], web.Hosts);
    }

    [Fact]
    public async Task RejectsAnExpiredCookieWithoutCallingSteam()
    {
        var web = new FakeWeb();
        var vault = new MemoryVault();
        using var session = NewSession(vault, web);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => session.SaveCookieAsync(Cookie("web:community", 1000000000), CancellationToken.None));

        Assert.Contains("expirou", error.Message);
        Assert.Null(vault.Value);
        Assert.Empty(web.Hosts);
    }

    [Fact]
    public async Task RestoresACookieWithoutCallingSteam()
    {
        var web = new FakeWeb();
        var cookie = Cookie("web:community", 2000000000);
        using var session = NewSession(new MemoryVault { Value = cookie }, web);

        Assert.True(session.Restore());
        var opened = await session.OpenAsync(CancellationToken.None);

        Assert.Equal(SteamId, opened.SteamId);
        Assert.True(session.CanReadBadges);
        Assert.Empty(web.Hosts);
    }

    [Fact]
    public async Task OnlyRevokesRefreshTokensOnLogout()
    {
        var web = new FakeWeb();
        var vault = new MemoryVault { Value = Cookie("web:community", 2000000000) };
        using var withCookie = NewSession(vault, web);
        withCookie.Restore();

        await withCookie.LogoutAsync();

        Assert.Null(vault.Value);
        Assert.Empty(web.Hosts);

        vault.Value = "eyJhbGciOiJFZERTQSJ9.e30.assinatura";
        using var withToken = NewSession(vault, web);
        withToken.Restore();

        await withToken.LogoutAsync();

        Assert.Null(vault.Value);
        Assert.Contains("login.steampowered.com", web.Hosts);
    }

    private static SessionService NewSession(MemoryVault vault, FakeWeb web) =>
        new(vault, new SteamAuthApi(web), new OwnedGamesClient(web));

    private static string Cookie(string audience, long expires)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($$"""{"sub":"{{SteamId}}","aud":["{{audience}}"],"exp":{{expires}}}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{SteamId}%7C%7CeyJhbGciOiJFZERTQSJ9.{payload}.assinatura";
    }

    private sealed class MemoryVault : ISessionVault
    {
        public string? Value { get; set; }

        public string? Read() => Value;

        public void Save(string refreshToken) => Value = refreshToken;

        public void Delete() => Value = null;
    }

    private sealed class FakeWeb : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(request.RequestUri.Host == "api.steampowered.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"response":{}}""") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"error":8}""") });
        }
    }
}
