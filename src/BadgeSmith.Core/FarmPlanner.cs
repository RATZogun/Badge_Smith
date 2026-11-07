namespace BadgeSmith.Core;

public static class FarmPlanner
{
    public static FarmStep? Next(IEnumerable<Badge> badges, FarmOptions options)
    {
        var pending = badges
            .Where(b => b.RemainingDrops > 0 && !options.Blacklist.Contains(b.AppId))
            .ToList();

        var ready = pending
            .Where(b => b.HoursPlayed >= options.HoursBeforeDrops)
            .OrderByDescending(b => b.HoursPlayed)
            .ThenBy(b => b.AppId)
            .FirstOrDefault();

        if (ready is not null)
        {
            var duration = ready.RemainingDrops == 1 ? options.LastDropDuration : options.SoloDuration;
            return new FarmStep(FarmMode.Solo, [ready], duration);
        }

        var young = pending
            .OrderByDescending(b => b.HoursPlayed)
            .ThenBy(b => b.AppId)
            .Take(options.MaxConcurrentGames)
            .ToList();

        return young.Count == 0 ? null : new FarmStep(FarmMode.Batch, young, options.BatchDuration);
    }
}
