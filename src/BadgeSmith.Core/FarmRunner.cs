namespace BadgeSmith.Core;

public abstract record FarmEvent
{
    public sealed record Scanned(int GamesWithDrops, int TotalDrops) : FarmEvent;

    public sealed record Farming(FarmStep Step, IReadOnlyList<uint> Started) : FarmEvent;

    public sealed record GameSkipped(uint AppId, string Reason) : FarmEvent;

    public sealed record ScanFailed(Exception Error, TimeSpan RetryIn) : FarmEvent;

    public sealed record Finished(IReadOnlyCollection<uint> Skipped) : FarmEvent;
}

public sealed class FarmRunner(
    Func<CancellationToken, Task<IReadOnlyList<Badge>>> scan,
    IAgentLauncher launcher,
    FarmOptions options)
{
    private static readonly TimeSpan MinScanRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxScanRetry = TimeSpan.FromMinutes(5);

    public async Task RunAsync(Action<FarmEvent> onEvent, CancellationToken cancellationToken)
    {
        var retry = MinScanRetry;
        var skipped = new HashSet<uint>();
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

                var pending = badges
                    .Where(b => b.RemainingDrops > 0 && !options.Blacklist.Contains(b.AppId) && !skipped.Contains(b.AppId))
                    .ToList();
                onEvent(new FarmEvent.Scanned(pending.Count, pending.Sum(b => b.RemainingDrops)));

                FarmStep? step;
                AgentStartResult result;
                do
                {
                    step = FarmPlanner.Next(pending.Where(b => !skipped.Contains(b.AppId)), options);
                    if (step is null)
                    {
                        onEvent(new FarmEvent.Finished(skipped));
                        return;
                    }

                    result = await launcher.StartAsync(step.Games.Select(g => g.AppId), cancellationToken);
                    foreach (var (appId, reason) in result.Failed)
                    {
                        skipped.Add(appId);
                        onEvent(new FarmEvent.GameSkipped(appId, reason));
                    }
                }
                while (result.Started.Count == 0);

                onEvent(new FarmEvent.Farming(step, result.Started));
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
