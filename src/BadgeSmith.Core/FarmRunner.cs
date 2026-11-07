namespace BadgeSmith.Core;

public abstract record FarmEvent
{
    public sealed record Scanned(int GamesWithDrops, int TotalDrops) : FarmEvent;

    public sealed record Farming(FarmStep Step, IReadOnlyList<uint> Started) : FarmEvent;

    public sealed record ScanFailed(Exception Error, TimeSpan RetryIn) : FarmEvent;

    public sealed record Finished : FarmEvent;
}

public sealed class FarmRunner(
    Func<CancellationToken, Task<IReadOnlyList<Badge>>> scan,
    AgentLauncher launcher,
    FarmOptions options)
{
    private static readonly TimeSpan MinScanRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxScanRetry = TimeSpan.FromMinutes(5);

    public async Task RunAsync(Action<FarmEvent> onEvent, CancellationToken cancellationToken)
    {
        var retry = MinScanRetry;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<Badge> badges;
                try
                {
                    badges = await scan(cancellationToken);
                    retry = MinScanRetry;
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    onEvent(new FarmEvent.ScanFailed(error, retry));
                    await Task.Delay(retry, cancellationToken);
                    retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, MaxScanRetry.Ticks));
                    continue;
                }

                var pending = badges.Where(b => b.RemainingDrops > 0 && !options.Blacklist.Contains(b.AppId)).ToList();
                onEvent(new FarmEvent.Scanned(pending.Count, pending.Sum(b => b.RemainingDrops)));

                var step = FarmPlanner.Next(pending, options);
                if (step is null)
                {
                    onEvent(new FarmEvent.Finished());
                    return;
                }

                var started = await launcher.StartAsync(step.Games.Select(g => g.AppId), cancellationToken);
                onEvent(new FarmEvent.Farming(step, started));
                try
                {
                    await Task.Delay(step.Duration, cancellationToken);
                }
                finally
                {
                    await launcher.StopAllAsync();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
