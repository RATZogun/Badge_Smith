using BadgeSmith.Core;

namespace BadgeSmith.Tests;

public class FarmRunnerTests
{
    private static readonly FarmOptions Options = new()
    {
        SoloDuration = TimeSpan.FromMilliseconds(10),
        LastDropDuration = TimeSpan.FromMilliseconds(10),
        BatchDuration = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public async Task SkipsAGameWhoseAgentFailsAndKeepsFarmingTheOthers()
    {
        var scans = new Queue<IReadOnlyList<Badge>>(
        [
            [new Badge(1, "Falha", 2, 5), new Badge(2, "Novo", 2, 0.5)],
            [new Badge(1, "Falha", 2, 5), new Badge(2, "Novo", 0, 0.6)],
        ]);
        var launcher = new FakeLauncher(failing: 1);
        var events = new List<FarmEvent>();

        await RunWithTimeout(new FarmRunner(_ => Task.FromResult(scans.Dequeue()), launcher, Options), events);

        Assert.Collection(
            events,
            e => Assert.Equal(2, Assert.IsType<FarmEvent.Scanned>(e).GamesWithDrops),
            e => Assert.Equal(1u, Assert.IsType<FarmEvent.GameSkipped>(e).AppId),
            e => Assert.Equal([2u], Assert.IsType<FarmEvent.Farming>(e).Started),
            e => Assert.Equal(0, Assert.IsType<FarmEvent.Scanned>(e).GamesWithDrops),
            e => Assert.Equal([1u], Assert.IsType<FarmEvent.Finished>(e).Skipped));
        Assert.Empty(scans);
        Assert.Equal(1, launcher.StopCalls);
    }

    [Fact]
    public async Task FinishesWhenNothingIsLeft()
    {
        var events = new List<FarmEvent>();

        await RunWithTimeout(
            new FarmRunner(_ => Task.FromResult<IReadOnlyList<Badge>>([new Badge(1, "A", 0, 3)]), new FakeLauncher(), Options),
            events);

        Assert.Collection(
            events,
            e => Assert.Equal(0, Assert.IsType<FarmEvent.Scanned>(e).GamesWithDrops),
            e => Assert.Empty(Assert.IsType<FarmEvent.Finished>(e).Skipped));
    }

    private static async Task RunWithTimeout(FarmRunner runner, List<FarmEvent> events)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runner.RunAsync(events.Add, timeout.Token);
        Assert.False(timeout.IsCancellationRequested, "O loop não terminou sozinho.");
    }

    private sealed class FakeLauncher(uint? failing = null) : IAgentLauncher
    {
        public int StopCalls { get; private set; }

        public Task<AgentStartResult> StartAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken)
        {
            var ids = appIds.ToList();
            var failed = ids.Where(id => id == failing).ToDictionary(id => id, _ => "não é dono do jogo");
            return Task.FromResult(new AgentStartResult(ids.Except(failed.Keys).ToList(), failed));
        }

        public Task StopAllAsync()
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }
}
