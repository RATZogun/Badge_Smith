namespace BadgeSmith.Core;

public sealed record Badge(uint AppId, string Name, int RemainingDrops, double HoursPlayed);
