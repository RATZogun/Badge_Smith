using BadgeSmith.Core;

namespace BadgeSmith.Tests;

public class FarmPlannerTests
{
    private static readonly FarmOptions Options = new();

    [Fact]
    public void ReturnsNullWhenNoDropsRemain()
    {
        var step = FarmPlanner.Next([new Badge(1, "A", 0, 10)], Options);

        Assert.Null(step);
    }

    [Fact]
    public void FarmsTheMostPlayedReadyGameAlone()
    {
        var step = FarmPlanner.Next(
            [new Badge(1, "Novo", 3, 0.5), new Badge(2, "Pronto", 4, 2.5), new Badge(3, "Mais jogado", 2, 8)],
            Options);

        Assert.NotNull(step);
        Assert.Equal(FarmMode.Solo, step.Mode);
        Assert.Equal(3u, Assert.Single(step.Games).AppId);
        Assert.Equal(Options.SoloDuration, step.Duration);
    }

    [Fact]
    public void UsesShorterCycleForTheLastDrop()
    {
        var step = FarmPlanner.Next([new Badge(1, "A", 1, 5)], Options);

        Assert.NotNull(step);
        Assert.Equal(Options.LastDropDuration, step.Duration);
    }

    [Fact]
    public void BatchesYoungGamesUpToTheLimit()
    {
        var badges = Enumerable.Range(1, 40).Select(i => new Badge((uint)i, $"J{i}", 2, i / 100.0)).ToList();

        var step = FarmPlanner.Next(badges, Options);

        Assert.NotNull(step);
        Assert.Equal(FarmMode.Batch, step.Mode);
        Assert.Equal(Options.MaxConcurrentGames, step.Games.Count);
        Assert.Equal(40u, step.Games[0].AppId);
        Assert.Equal(Options.BatchDuration, step.Duration);
    }

    [Fact]
    public void SkipsBlacklistedGames()
    {
        var options = Options with { Blacklist = new HashSet<uint> { 2 } };

        var step = FarmPlanner.Next([new Badge(1, "A", 2, 1), new Badge(2, "B", 2, 9)], options);

        Assert.NotNull(step);
        Assert.Equal(FarmMode.Batch, step.Mode);
        Assert.Equal(1u, Assert.Single(step.Games).AppId);
    }
}
