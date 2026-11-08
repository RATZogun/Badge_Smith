namespace BadgeSmith.Steam;

public sealed record SteamLoginResult(string RefreshToken, string AccountName);

public delegate Task<string> SteamGuardCodePrompt(SteamGuardType type, bool previousWasWrong, CancellationToken cancellationToken);

public sealed class SteamLogin(SteamAuthApi api, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly int[] WrongCodeResults = [65, 88];

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<SteamLoginResult> WithQrAsync(Action<string> showChallenge, CancellationToken cancellationToken)
    {
        var session = await api.BeginWithQrAsync(cancellationToken);
        showChallenge(session.ChallengeUrl!);
        return await WaitAsync(session, showChallenge, cancellationToken);
    }

    public async Task<SteamLoginResult> WithCredentialsAsync(
        string accountName,
        string password,
        SteamGuardCodePrompt askCode,
        Action<SteamGuardType> waitingConfirmation,
        CancellationToken cancellationToken)
    {
        var session = await api.BeginWithCredentialsAsync(accountName, password, cancellationToken);
        var codeType = session.Confirmations.FirstOrDefault(t => t is SteamGuardType.DeviceCode or SteamGuardType.EmailCode);
        var confirmationType = session.Confirmations.FirstOrDefault(t => t is SteamGuardType.DeviceConfirmation or SteamGuardType.EmailConfirmation);

        if (codeType != SteamGuardType.Unknown)
        {
            var wrong = false;
            while (true)
            {
                var code = await askCode(codeType, wrong, cancellationToken);
                try
                {
                    await api.SubmitGuardCodeAsync(session, code.Trim(), codeType, cancellationToken);
                    break;
                }
                catch (SteamAuthException error) when (WrongCodeResults.Contains(error.Result))
                {
                    wrong = true;
                }
            }
        }
        else if (confirmationType != SteamGuardType.Unknown)
        {
            waitingConfirmation(confirmationType);
        }

        return await WaitAsync(session, null, cancellationToken);
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
