using BadgeSmith.Core;
using BadgeSmith.Desktop.ViewModels;
using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class AchievementsViewModelTests
{
    [Fact]
    public async Task KeepsTheProgressOfTheOpenedGameWhenTheLibraryProgressArrivesLater()
    {
        var libraryProgress = new TaskCompletionSource<IReadOnlyDictionary<uint, AchievementProgress>>();
        var achievements = new AchievementsViewModel(
            _ => Task.FromResult<IReadOnlyList<OwnedGame>>([new OwnedGame(10, "Jogo", 1, true)]),
            new AchievementService(new FakeAgent([State("A", true), State("B", false)])),
            loadProgress: _ => libraryProgress.Task);

        await achievements.LoadGamesCommand.ExecuteAsync(null);
        var game = Assert.Single(achievements.Games);
        achievements.SelectedGame = game;
        await WaitUntil(() => game.Progress is not null);
        libraryProgress.SetResult(new Dictionary<uint, AchievementProgress> { [10] = new(0, 2) });

        Assert.Equal(new AchievementProgress(1, 2), game.Progress);
    }

    [Fact]
    public async Task ShowsTheLibraryProgressOfGamesNotOpenedYet()
    {
        var achievements = new AchievementsViewModel(
            _ => Task.FromResult<IReadOnlyList<OwnedGame>>([new OwnedGame(10, "Jogo", 1, true)]),
            new AchievementService(new FakeAgent([])),
            loadProgress: _ => Task.FromResult<IReadOnlyDictionary<uint, AchievementProgress>>(
                new Dictionary<uint, AchievementProgress> { [10] = new(3, 7) }));

        await achievements.LoadGamesCommand.ExecuteAsync(null);

        Assert.Equal(new AchievementProgress(3, 7), Assert.Single(achievements.Games).Progress);
    }

    private static AchievementState State(string api, bool achieved) =>
        new(api, api, "", false, false, achieved, null);

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeAgent(IReadOnlyList<AchievementState> achievements) : IAchievementAgent
    {
        public Task<IReadOnlyList<AchievementState>> ListAsync(uint appId, string language, CancellationToken cancellationToken) =>
            Task.FromResult(achievements);

        public Task<IReadOnlyList<AchievementChange>> ChangeAsync(uint appId, IReadOnlyList<string> apiNames, bool unlock, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AchievementChange>>([]);
    }
}
