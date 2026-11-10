namespace BadgeSmith.Steam;

public sealed record SteamLoginResult(string RefreshToken, string AccountName);

public sealed class SteamLogin(SteamAuthApi api, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<SteamLoginResult> WithQrAsync(Action<string> showChallenge, CancellationToken cancellationToken)
    {
        var session = await api.BeginWithQrAsync(cancellationToken);
        showChallenge(session.ChallengeUrl!);
        return await WaitAsync(session, showChallenge, cancellationToken);
    }

    private async Task<SteamLoginResult> WaitAsync(SteamAuthSession session, Action<string>? showChallenge, CancellationToken cancellationToken)
    {
        var clientId = session.ClientId;
        while (true)
        {
            var poll = await api.PollAsync(clientId, session.RequestId, cancellationToken);
            if (poll.RefreshToken is { } refreshToken)
            {
                return new SteamLoginResult(refreshToken, poll.AccountName ?? "");
            }

            if (poll.NewClientId is { } newClientId)
            {
                clientId = newClientId;
            }

            if (poll.NewChallengeUrl is { } challenge)
            {
                showChallenge?.Invoke(challenge);
            }

            await _delay(session.Interval, cancellationToken);
        }
    }
}
