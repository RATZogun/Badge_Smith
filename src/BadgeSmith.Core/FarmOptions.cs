namespace BadgeSmith.Core;

public sealed record FarmOptions
{
    public double HoursBeforeDrops { get; init; } = 2;
    public int MaxConcurrentGames { get; init; } = 30;
    public TimeSpan SoloDuration { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan LastDropDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan BatchDuration { get; init; } = TimeSpan.FromMinutes(6);
    public IReadOnlySet<uint> Blacklist { get; init; } = new HashSet<uint>();
}
