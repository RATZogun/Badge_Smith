using System.Net;
using System.Text;
using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class SteamSessionTests
{
    private const string Cookie = "76561198000000001%7C%7Caccess.token.value";

    [Fact]
    public async Task ExchangesTheRefreshTokenForTheCommunityCookie()
    {
        var steam = new FakeWeb();
        using var api = new SteamAuthApi(steam);

        var cookie = await api.GetLoginCookieAsync("refresh", CancellationToken.None);

        Assert.Equal(Cookie, cookie);
        Assert.Equal(["login.steampowered.com", "steamcommunity.com"], steam.Hosts);
        Assert.Contains("refresh", steam.Bodies[0]);
        Assert.Contains("76561198000000001", steam.Bodies[1]);
    }

    [Theory]
    [InlineData("<html>manutenção</html>")]
    [InlineData("""{"error":"invalid"}""")]
    [InlineData("""{"steamID":"1","transfer_info":{}}""")]
    public async Task RejectsAnUnexpectedFinalizeResponse(string body)
    {
        var steam = new FakeWeb { FinalizeBody = body };
        using var api = new SteamAuthApi(steam);

        await Assert.ThrowsAsync<SteamAuthException>(() => api.GetLoginCookieAsync("refresh", CancellationToken.None));
    }

    [Fact]
    public async Task RevokesTheRefreshTokenWithTheSessionAccessToken()
    {
        var steam = new FakeWeb();
        using var api = new SteamAuthApi(steam);

        await api.RevokeAsync("refresh", CancellationToken.None);

        var revoke = steam.Requests.Last();
        Assert.Equal("/IAuthenticationService/RevokeToken/v1", revoke.AbsolutePath);
        Assert.Equal("?access_token=access.token.value", revoke.Query);
    }

    [Fact]
    public void ReadsTheSteamIdFromTheToken()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"sub":"76561198000000001","aud":["web"]}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(76561198000000001UL, SteamAuthApi.SteamIdFromToken($"eyJhbGciOiJFZERTQSJ9.{payload}.signature"));
        Assert.Null(SteamAuthApi.SteamIdFromToken("não é um token"));
    }

    private sealed class FakeWeb : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> Hosts => Requests.Where(r => r.Host != "api.steampowered.com").Select(r => r.Host).ToList();
        public List<string> Bodies { get; } = [];
        public string? FinalizeBody { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            switch (uri.Host)
            {
                case "login.steampowered.com":
                    response.Content = new StringContent(FinalizeBody ?? """
                        {"steamID":"76561198000000001","transfer_info":[
                          {"url":"https://help.steampowered.com/login/settoken","params":{"nonce":"n1","auth":"a1"}},
                          {"url":"https://steamcommunity.com/login/settoken","params":{"nonce":"n2","auth":"a2"}}]}
                        """);
                    break;
                case "steamcommunity.com":
                    response.Headers.Add("Set-Cookie", $"steamLoginSecure={Cookie}; Path=/; Secure; HttpOnly");
                    break;
                default:
                    response.Headers.Add("x-eresult", "1");
                    response.Content = new ByteArrayContent([]);
                    break;
            }

            return response;
        }
    }
}
