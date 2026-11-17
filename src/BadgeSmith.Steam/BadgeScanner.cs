using BadgeSmith.Core;

namespace BadgeSmith.Steam;

public sealed class BadgeScanner(SteamWebSession session) : IBadgeScanner
{
    private static readonly TimeSpan DelayBetweenRequests = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        CookieContainer = session.Cookies,
        AllowAutoRedirect = true,
    })
    {
        BaseAddress = new Uri("https://steamcommunity.com/"),
        Timeout = TimeSpan.FromSeconds(30),
    };

    public bool? AccountLimited { get; private set; }

    public async Task<IReadOnlyList<Badge>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var badges = new List<Badge>();
        var withoutDropInfo = new List<Badge>();
        var pageCount = 1;

        for (var page = 1; page <= pageCount; page++)
        {
            if (page > 1)
            {
                await Task.Delay(DelayBetweenRequests, cancellationToken);
            }

            var result = BadgePageParser.Parse(await GetSignedInAsync($"profiles/{session.SteamId}/badges/?l=english&p={page}", cancellationToken));
            badges.AddRange(result.Badges);
            withoutDropInfo.AddRange(result.WithoutDropInfo);
            pageCount = result.PageCount;
            AccountLimited = result.AccountLimited ?? AccountLimited;
        }

        foreach (var badge in withoutDropInfo)
        {
            await Task.Delay(DelayBetweenRequests, cancellationToken);
            var html = await GetSignedInAsync($"profiles/{session.SteamId}/gamecards/{badge.AppId}/?l=english", cancellationToken);
            if (BadgePageParser.ParseRemainingDrops(html) is { } remaining)
            {
                badges.Add(badge with { RemainingDrops = remaining });
            }
        }

        return badges;
    }

    public void Dispose() => _http.Dispose();

    private async Task<string> GetSignedInAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        if (response.RequestMessage?.RequestUri?.AbsolutePath.Contains("/login", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new SteamSessionExpiredException();
        }

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (BadgePageParser.ParseSignedInSteamId(html) != session.SteamId)
        {
            throw new SteamSessionExpiredException();
        }

        return html;
    }
}

public sealed class SteamSessionExpiredException()
    : Exception("A sessão da Steam expirou ou o cookie steamLoginSecure é inválido.");
