using BadgeSmith.Steam;

namespace BadgeSmith.Desktop.Services;

public sealed class SessionService : IDisposable
{
    private readonly ISessionVault _vault = SessionVault.ForCurrentSystem();
    private readonly OwnedGamesClient _check = new();
    private string? _stored;

    public SteamAuthApi Auth { get; } = new();

    public ulong? SteamId => _stored switch
    {
        null => null,
        var cookie when SteamWebSession.IsCookie(cookie) => new SteamWebSession(cookie).SteamId,
        var token => SteamAuthApi.SteamIdFromToken(token),
    };

    public bool CanReadBadges => _stored is null || !SteamWebSession.IsCookie(_stored) || new SteamWebSession(_stored).IsForCommunity;

    public bool Restore()
    {
        _stored = _vault.Read();
        return _stored is not null;
    }

    public async Task<SteamWebSession> OpenAsync(CancellationToken cancellationToken) => _stored switch
    {
        null => throw new SteamSessionExpiredException(),
        var cookie when SteamWebSession.IsCookie(cookie) => new SteamWebSession(cookie),
        var token => new SteamWebSession(await Auth.GetLoginCookieAsync(token, cancellationToken)),
    };

    public async Task<SteamWebSession> SaveAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = new SteamWebSession(await Auth.GetLoginCookieAsync(refreshToken, cancellationToken));
        await ReplaceAsync(refreshToken);
        return session;
    }

    public async Task<SteamWebSession> SaveCookieAsync(string cookie, CancellationToken cancellationToken)
    {
        var value = cookie.Trim();
        if (value.StartsWith("steamLoginSecure=", StringComparison.Ordinal))
        {
            value = value["steamLoginSecure=".Length..];
        }

        var session = new SteamWebSession(value);
        if (session.Problem(DateTimeOffset.UtcNow) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        await _check.GetAsync(session, cancellationToken);
        await ReplaceAsync(value);
        return session;
    }

    public async Task LogoutAsync()
    {
        var stored = _stored ?? _vault.Read();
        _vault.Delete();
        _stored = null;
        if (stored is not null && !SteamWebSession.IsCookie(stored))
        {
            await TryRevokeAsync(stored);
        }
    }

    public void Dispose()
    {
        Auth.Dispose();
        _check.Dispose();
    }

    private async Task ReplaceAsync(string value)
    {
        var previous = _vault.Read();
        _vault.Save(value);
        _stored = value;
        if (previous is not null && previous != value && !SteamWebSession.IsCookie(previous))
        {
            await TryRevokeAsync(previous);
        }
    }

    private async Task TryRevokeAsync(string token)
    {
        try
        {
            await Auth.RevokeAsync(token, CancellationToken.None);
        }
        catch (Exception error) when (error is SteamAuthException or HttpRequestException or TaskCanceledException)
        {
        }
    }
}
