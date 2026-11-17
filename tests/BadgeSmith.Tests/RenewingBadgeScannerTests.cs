using BadgeSmith.Core;
using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class RenewingBadgeScannerTests
{
    private const string Cookie = "76561198000000001%7C%7Caccess.token.value";

    [Fact]
    public async Task RenewsTheSessionOnceWhenItExpires()
    {
        var opened = 0;
        var first = new FakeScanner(expired: false);
        var scanners = new Queue<FakeScanner>([first, new FakeScanner(expired: false)]);
        using var scanner = new RenewingBadgeScanner(
            _ =>
            {
                opened++;
                return Task.FromResult(new SteamWebSession(Cookie));
            },
            _ => scanners.Dequeue());

        await scanner.ScanAsync();
        Assert.Equal(1, opened);

        first.Expire();
        await scanner.ScanAsync();
        Assert.Equal(2, opened);
        Assert.Empty(scanners);
    }

    [Fact]
    public async Task GivesUpWhenAFreshSessionIsAlreadyExpired()
    {
        var opened = 0;
        using var scanner = new RenewingBadgeScanner(
            _ =>
            {
                opened++;
                return Task.FromResult(new SteamWebSession(Cookie));
            },
            _ => new FakeScanner(expired: true));

        await Assert.ThrowsAsync<SteamSessionExpiredException>(() => scanner.ScanAsync());
        Assert.Equal(1, opened);
    }

    private sealed class FakeScanner(bool expired) : IBadgeScanner
    {
        private bool _expired = expired;

        public bool? AccountLimited => null;

        public void Expire() => _expired = true;

        public Task<IReadOnlyList<Badge>> ScanAsync(CancellationToken cancellationToken = default) =>
            _expired ? throw new SteamSessionExpiredException() : Task.FromResult<IReadOnlyList<Badge>>([]);

        public void Dispose()
        {
        }
    }
}
