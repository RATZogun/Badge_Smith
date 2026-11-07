using BadgeSmith.Core;

namespace BadgeSmith.Steam;

public sealed class BadgeScanner(SteamWebSession session) : IDisposable
{
    private static readonly TimeSpan DelayBetweenPages = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        CookieContainer = session.Cookies,
        AllowAutoRedirect = true,
    })
    {
        BaseAddress = new Uri("https://steamcommunity.com/"),
        Timeout = TimeSpan.FromSeconds(30),
    };

    public async Task<IReadOnlyList<Badge>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var badges = new List<Badge>();
        var pageCount = 1;

        for (var page = 1; page <= pageCount; page++)
        {
            if (page > 1)
            {
                await Task.Delay(DelayBetweenPages, cancellationToken);
            }

            using var response = await _http.GetAsync($"profiles/{session.SteamId}/badges/?l=english&p={page}", cancellationToken);
            if (response.RequestMessage?.RequestUri?.AbsolutePath.Contains("/login", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new SteamSessionExpiredException();
            }

            response.EnsureSuccessStatusCode();
            var result = BadgePageParser.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!result.IsSignedIn)
            {
                throw new SteamSessionExpiredException();
            }

            badges.AddRange(result.Badges);
            pageCount = result.PageCount;
        }

        return badges;
    }

    public void Dispose() => _http.Dispose();
}

public sealed class SteamSessionExpiredException()
    : Exception("A sessão da Steam expirou ou o cookie steamLoginSecure é inválido.");
