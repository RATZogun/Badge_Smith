namespace BadgeSmith.Core;

public abstract record FarmEvent
{
    public sealed record Scanned(IReadOnlyList<Badge> WithDrops, IReadOnlyList<Badge> Pending) : FarmEvent
    {
        public int GamesWithDrops => Pending.Count;

        public int TotalDrops => Pending.Sum(b => b.RemainingDrops);
    }

    public sealed record Farming(FarmStep Step, IReadOnlyList<uint> Started) : FarmEvent;

    public sealed record GameSkipped(uint AppId, string Reason) : FarmEvent;

    public sealed record ScanFailed(Exception Error, TimeSpan RetryIn) : FarmEvent;

    public sealed record SteamUnavailable(string Reason, TimeSpan RetryIn) : FarmEvent;

    public sealed record Finished(IReadOnlyCollection<uint> Skipped) : FarmEvent;
}

public sealed class FarmRunner(
    Func<CancellationToken, Task<IReadOnlyList<Badge>>> scan,
    IAgentLauncher launcher,
    FarmOptions options)
{
    public async Task RunAsync(Action<FarmEvent> onEvent, CancellationToken cancellationToken)
    {
        var scanRetry = options.MinRetry;
        var steamRetry = options.MinRetry;
        var skipped = new HashSet<uint>();
        var knownHours = new Dictionary<uint, double>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<Badge> badges;
                try
                {
                    badges = await scan(cancellationToken);
                    scanRetry = options.MinRetry;
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    onEvent(new FarmEvent.ScanFailed(error, scanRetry));
                    await Task.Delay(scanRetry, cancellationToken);
                    scanRetry = Next(scanRetry);
                    continue;
                }

                var withDrops = badges
                    .Where(b => b.RemainingDrops > 0)
                    .Select(b => b with { HoursPlayed = knownHours[b.AppId] = Math.Max(b.HoursPlayed, knownHours.GetValueOrDefault(b.AppId)) })
                    .ToList();
                var pending = withDrops
                    .Where(b => !options.Blacklist.Contains(b.AppId) && !skipped.Contains(b.AppId))
                    .ToList();
                onEvent(new FarmEvent.Scanned(withDrops, pending));

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
                while (result.Started.Count == 0 && result.SteamUnavailable is null);

                if (result.SteamUnavailable is { } unavailable)
                {
                    await launcher.StopAllAsync();
                    onEvent(new FarmEvent.SteamUnavailable(unavailable, steamRetry));
                    await Task.Delay(steamRetry, cancellationToken);
                    steamRetry = Next(steamRetry);
                    continue;
                }

                steamRetry = options.MinRetry;
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

    private TimeSpan Next(TimeSpan retry) => TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, options.MaxRetry.Ticks));
}
