namespace BadgeSmith.Core;

public enum FarmMode
{
    Solo,
    Batch,
}

public sealed record FarmStep(FarmMode Mode, IReadOnlyList<Badge> Games, TimeSpan Duration);
