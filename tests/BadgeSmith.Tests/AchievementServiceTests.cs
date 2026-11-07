using BadgeSmith.Core;

namespace BadgeSmith.Tests;

public class AchievementServiceTests
{
    private static readonly AchievementState[] Achievements =
    [
        State("A", achieved: false),
        State("B", achieved: false),
        State("C", achieved: true),
        State("P", achieved: false, isProtected: true),
    ];

    [Fact]
    public async Task ChangesAllPendingAchievementsInOneCallWithoutDelay()
    {
        var agent = new FakeAgent(Achievements);
        var service = new AchievementService(agent, (_, _) => throw new InvalidOperationException("não deveria esperar"));

        var results = await service.SetAsync(1, ["A", "B"], unlock: true, new AchievementChangeOptions(), null, CancellationToken.None);

        Assert.Equal([["A", "B"]], agent.Calls);
        Assert.All(results, r => Assert.True(r.Ok));
    }

    [Fact]
    public async Task SkipsAlreadyChangedProtectedAndUnknownAchievements()
    {
        var agent = new FakeAgent(Achievements);
        var reported = new List<AchievementChange>();
        var service = new AchievementService(agent);

        var results = await service.SetAsync(1, ["C", "P", "X", "C"], unlock: true, new AchievementChangeOptions(), new SyncProgress(reported.Add), CancellationToken.None);

        Assert.Empty(agent.Calls);
        Assert.Collection(
            results,
            r => Assert.Equal(("C", true), (r.Api, r.Ok)),
            r => Assert.Equal(("P", false), (r.Api, r.Ok)),
            r => Assert.Equal(("X", false), (r.Api, r.Ok)));
        Assert.Equal(results, reported);
    }

    [Fact]
    public async Task LocksOnlyUnlockedAchievements()
    {
        var agent = new FakeAgent(Achievements);
        var service = new AchievementService(agent);

        await service.SetAsync(1, ["A", "C"], unlock: false, new AchievementChangeOptions(), null, CancellationToken.None);

        Assert.Equal([["C"]], agent.Calls);
        Assert.Equal([false], agent.UnlockFlags);
    }

    [Fact]
    public async Task WaitsARandomDelayBetweenAchievementsWhenAsked()
    {
        var agent = new FakeAgent(Achievements);
        var delays = new List<TimeSpan>();
        var service = new AchievementService(agent, (d, _) =>
        {
            delays.Add(d);
            return Task.CompletedTask;
        });
        var options = new AchievementChangeOptions { MinDelay = TimeSpan.FromSeconds(10), MaxDelay = TimeSpan.FromSeconds(30) };

        await service.SetAsync(1, ["A", "B"], unlock: true, options, null, CancellationToken.None);

        Assert.Equal([["A"], ["B"]], agent.Calls);
        var delay = Assert.Single(delays);
        Assert.InRange(delay, options.MinDelay, options.MaxDelay);
    }

    private static AchievementState State(string api, bool achieved, bool isProtected = false) =>
        new(api, api, "", false, isProtected, achieved, null);

    private sealed class SyncProgress(Action<AchievementChange> report) : IProgress<AchievementChange>
    {
        public void Report(AchievementChange value) => report(value);
    }

    private sealed class FakeAgent(IReadOnlyList<AchievementState> achievements) : IAchievementAgent
    {
        public List<string[]> Calls { get; } = [];
        public List<bool> UnlockFlags { get; } = [];

        public Task<IReadOnlyList<AchievementState>> ListAsync(uint appId, string language, CancellationToken cancellationToken) =>
            Task.FromResult(achievements);

        public Task<IReadOnlyList<AchievementChange>> ChangeAsync(uint appId, IReadOnlyList<string> apiNames, bool unlock, CancellationToken cancellationToken)
        {
            Calls.Add([.. apiNames]);
            UnlockFlags.Add(unlock);
            return Task.FromResult<IReadOnlyList<AchievementChange>>(apiNames.Select(a => new AchievementChange(a, true, null)).ToList());
        }
    }
}
