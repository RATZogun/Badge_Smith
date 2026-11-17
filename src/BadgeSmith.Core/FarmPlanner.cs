namespace BadgeSmith.Core;

public static class FarmPlanner
{
    public static FarmStep? Next(IEnumerable<Badge> badges, FarmOptions options)
    {
        var pending = badges
            .Where(b => b.RemainingDrops > 0 && !options.Blacklist.Contains(b.AppId))
            .ToList();

        var young = pending
            .Where(b => b.HoursPlayed < options.HoursBeforeDrops)
            .OrderByDescending(b => b.HoursPlayed)
            .ThenBy(b => b.AppId)
            .Take(options.MaxConcurrentGames)
            .ToList();

        if (young.Count > 0)
        {
            return new FarmStep(FarmMode.Batch, young, options.BatchDuration);
        }

        var ready = pending
            .OrderByDescending(b => b.HoursPlayed)
            .ThenBy(b => b.AppId)
            .FirstOrDefault();

        return ready is null
            ? null
            : new FarmStep(FarmMode.Solo, [ready], ready.RemainingDrops == 1 ? options.LastDropDuration : options.SoloDuration);
    }
}
