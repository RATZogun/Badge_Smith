using BadgeSmith.Core;

namespace BadgeSmith.Steam;

public sealed class RenewingBadgeScanner(Func<CancellationToken, Task<SteamWebSession>> openSession) : IDisposable
{
    private BadgeScanner? _scanner;

    public async Task<IReadOnlyList<Badge>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var renewed = false;
        if (_scanner is null)
        {
            await RenewAsync(cancellationToken);
            renewed = true;
        }

        try
        {
            return await _scanner!.ScanAsync(cancellationToken);
        }
        catch (SteamSessionExpiredException) when (!renewed)
        {
            await RenewAsync(cancellationToken);
            return await _scanner!.ScanAsync(cancellationToken);
        }
    }

    public void Dispose() => _scanner?.Dispose();

    private async Task RenewAsync(CancellationToken cancellationToken)
    {
        var session = await openSession(cancellationToken);
        _scanner?.Dispose();
        _scanner = new BadgeScanner(session);
    }
}
