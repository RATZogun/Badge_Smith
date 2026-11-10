using System.Net;
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
