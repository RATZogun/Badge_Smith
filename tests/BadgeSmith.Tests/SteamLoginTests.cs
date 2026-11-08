using System.Net;
using System.Security.Cryptography;
using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class SteamLoginTests
{
    [Fact]
    public async Task QrLoginShowsEachChallengeAndReturnsTheRefreshToken()
    {
        var steam = new FakeSteam();
        steam.Reply("BeginAuthSessionViaQR", new ProtoWriter().Varint(1, 10).String(2, "https://s.team/q/1/a").Bytes(3, [7]).ToArray());
        steam.Reply("PollAuthSessionStatus", []);
        steam.Reply("PollAuthSessionStatus", new ProtoWriter().Varint(1, 11).String(2, "https://s.team/q/1/b").ToArray());
        steam.Reply("PollAuthSessionStatus", new ProtoWriter().String(3, "refresh").String(6, "conta").ToArray());
        var challenges = new List<string>();
        using var api = new SteamAuthApi(steam);

        var result = await new SteamLogin(api, NoDelay).WithQrAsync(challenges.Add, CancellationToken.None);

        Assert.Equal(new SteamLoginResult("refresh", "conta"), result);
        Assert.Equal(["https://s.team/q/1/a", "https://s.team/q/1/b"], challenges);
        var polls = steam.Requests.Where(r => r.Method == "PollAuthSessionStatus").Select(r => r.Body.Varint(1));
        Assert.Equal([10UL, 10UL, 11UL], polls);
    }

    [Fact]
    public async Task CredentialsLoginAsksTheCodeAgainWhenItIsWrong()
    {
        var steam = new FakeSteam();
        using var rsa = RSA.Create(2048);
        var key = rsa.ExportParameters(false);
        steam.Reply("GetPasswordRSAPublicKey", new ProtoWriter().String(1, Convert.ToHexString(key.Modulus!)).String(2, Convert.ToHexString(key.Exponent!)).Varint(3, 99).ToArray());
        steam.Reply("BeginAuthSessionViaCredentials", new ProtoWriter()
            .Varint(1, 20)
            .Bytes(2, [8])
            .Message(4, new ProtoWriter().Varint(1, (ulong)SteamGuardType.DeviceConfirmation))
            .Message(4, new ProtoWriter().Varint(1, (ulong)SteamGuardType.DeviceCode))
            .Varint(5, 76561198000000001)
            .ToArray());
        steam.Fail("UpdateAuthSessionWithSteamGuardCode", 88);
        steam.Reply("UpdateAuthSessionWithSteamGuardCode", []);
        steam.Reply("PollAuthSessionStatus", new ProtoWriter().String(3, "refresh").String(6, "conta").ToArray());
        var prompts = new List<(SteamGuardType, bool)>();
        using var api = new SteamAuthApi(steam);

        var result = await new SteamLogin(api, NoDelay).WithCredentialsAsync(
            "conta",
            "senha",
            (type, wrong, _) =>
            {
                prompts.Add((type, wrong));
                return Task.FromResult(wrong ? " 23456 " : "11111");
            },
            _ => Assert.Fail("não deveria esperar confirmação quando há código"),
            CancellationToken.None);

        Assert.Equal("refresh", result.RefreshToken);
        Assert.Equal([(SteamGuardType.DeviceCode, false), (SteamGuardType.DeviceCode, true)], prompts);
        var begin = steam.Requests.Single(r => r.Method == "BeginAuthSessionViaCredentials").Body;
        Assert.Equal("senha", System.Text.Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(begin.String(3)), RSAEncryptionPadding.Pkcs1)));
        Assert.Equal(99UL, begin.Varint(4));
        var lastCode = steam.Requests.Last(r => r.Method == "UpdateAuthSessionWithSteamGuardCode").Body;
        Assert.Equal(("23456", 76561198000000001UL, (ulong)SteamGuardType.DeviceCode), (lastCode.String(3), lastCode.Varint(2), lastCode.Varint(4)));
    }

    [Fact]
    public async Task CredentialsLoginWaitsForTheAppConfirmationWhenThereIsNoCode()
    {
        var steam = new FakeSteam();
        using var rsa = RSA.Create(2048);
        var key = rsa.ExportParameters(false);
        steam.Reply("GetPasswordRSAPublicKey", new ProtoWriter().String(1, Convert.ToHexString(key.Modulus!)).String(2, Convert.ToHexString(key.Exponent!)).ToArray());
        steam.Reply("BeginAuthSessionViaCredentials", new ProtoWriter().Varint(1, 20).Bytes(2, [8]).Message(4, new ProtoWriter().Varint(1, (ulong)SteamGuardType.DeviceConfirmation)).ToArray());
        steam.Reply("PollAuthSessionStatus", []);
        steam.Reply("PollAuthSessionStatus", new ProtoWriter().String(3, "refresh").ToArray());
        var waited = new List<SteamGuardType>();
        using var api = new SteamAuthApi(steam);

        var result = await new SteamLogin(api, NoDelay).WithCredentialsAsync(
            "conta",
            "senha",
            (_, _, _) => throw new InvalidOperationException("não deveria pedir código"),
            waited.Add,
            CancellationToken.None);

        Assert.Equal("refresh", result.RefreshToken);
        Assert.Equal([SteamGuardType.DeviceConfirmation], waited);
    }

    [Fact]
    public async Task ReportsAWrongPassword()
    {
        var steam = new FakeSteam();
        using var rsa = RSA.Create(2048);
        var key = rsa.ExportParameters(false);
        steam.Reply("GetPasswordRSAPublicKey", new ProtoWriter().String(1, Convert.ToHexString(key.Modulus!)).String(2, Convert.ToHexString(key.Exponent!)).ToArray());
        steam.Fail("BeginAuthSessionViaCredentials", 5);
        using var api = new SteamAuthApi(steam);

        var error = await Assert.ThrowsAsync<SteamAuthException>(() => api.BeginWithCredentialsAsync("conta", "errada", CancellationToken.None));

        Assert.Equal(5, error.Result);
    }

    [Fact]
    public void FindsTheLoginCookieAmongTheSetCookieHeaders()
    {
        string[] headers =
        [
            "sessionid=abc; Path=/",
            "steamLoginSecure=deleted; expires=Thu, 01 Jan 1970 00:00:00 GMT",
            "steamLoginSecure=76561198000000001%7C%7Ceyj; Path=/; Secure; HttpOnly",
        ];

        Assert.Equal("76561198000000001%7C%7Ceyj", SteamAuthApi.LoginCookie(headers));
        Assert.Null(SteamAuthApi.LoginCookie(["sessionid=abc"]));
    }

    private static Task NoDelay(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed record Request(string Method, ProtoMessage Body);

    private sealed class FakeSteam : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<(int Result, byte[] Body)>> _replies = [];

        public List<Request> Requests { get; } = [];

        public void Reply(string method, byte[] body) => Enqueue(method, 1, body);

        public void Fail(string method, int result) => Enqueue(method, result, []);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.RequestUri!.Segments[^2].TrimEnd('/');
            var encoded = request.Method == HttpMethod.Get
                ? Uri.UnescapeDataString(request.RequestUri.Query.Split('=', 2)[1])
                : (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('=', 2)[1];
            Requests.Add(new Request(method, ProtoMessage.Parse(Convert.FromBase64String(Uri.UnescapeDataString(encoded)))));

            var (result, body) = _replies[method].Dequeue();
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            response.Headers.Add("x-eresult", result.ToString());
            return response;
        }

        private void Enqueue(string method, int result, byte[] body)
        {
            if (!_replies.TryGetValue(method, out var queue))
            {
                _replies[method] = queue = new Queue<(int, byte[])>();
            }

            queue.Enqueue((result, body));
        }
    }
}
