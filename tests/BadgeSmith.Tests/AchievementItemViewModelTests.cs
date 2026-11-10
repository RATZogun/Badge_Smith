using BadgeSmith.Core;
using BadgeSmith.Desktop.ViewModels;
using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class AchievementItemViewModelTests
{
    [Fact]
    public void TracksPendingChanges()
    {
        var item = new AchievementItemViewModel(new AchievementState("A", "Primeira", "Desc", false, false, false, null));
        Assert.False(item.IsChanged);

        item.IsChecked = true;

        Assert.True(item.IsChanged);
        Assert.Equal("Bloqueada", item.Status);
    }

    [Fact]
    public void DescribesProtectedAndHiddenAchievements()
    {
        var item = new AchievementItemViewModel(new AchievementState("SECRET", "", "", true, true, true, null));

        Assert.False(item.CanChange);
        Assert.Equal("SECRET", item.Name);
        Assert.Equal("Conquista oculta", item.Description);
        Assert.Equal("Protegida: só o jogo pode alterar", item.Status);
    }

    [Fact]
    public async Task OffersAnAppIdThatIsNotInTheLibrary()
    {
        var achievements = new AchievementsViewModel(
            _ => Task.FromResult<IReadOnlyList<OwnedGame>>([new OwnedGame(10, "Jogo", 1, true)]),
            new AchievementService(new NoAgent()));
        await achievements.LoadGamesCommand.ExecuteAsync(null);

        achievements.Filter = "5257350";

        var game = Assert.Single(achievements.Games);
        Assert.Equal((5257350u, "App 5257350"), (game.AppId, game.Name));

        achievements.Filter = "10";
        Assert.Equal(10u, Assert.Single(achievements.Games).AppId);
    }

    [Fact]
    public async Task ShowsTheProgressOfEachGame()
    {
        var achievements = new AchievementsViewModel(
            _ => Task.FromResult<IReadOnlyList<OwnedGame>>([new OwnedGame(10, "Jogo", 1, true), new OwnedGame(20, "Outro", 1, true)]),
            new AchievementService(new NoAgent()),
            loadProgress: _ => Task.FromResult<IReadOnlyDictionary<uint, AchievementProgress>>(new Dictionary<uint, AchievementProgress> { [10] = new(22, 48) }));

        await achievements.LoadGamesCommand.ExecuteAsync(null);

        Assert.Equal("22/48", achievements.Games.Single(g => g.AppId == 10).ProgressText);
        Assert.False(achievements.Games.Single(g => g.AppId == 20).HasProgress);
    }

    private sealed class NoAgent : IAchievementAgent
    {
        public Task<IReadOnlyList<AchievementState>> ListAsync(uint appId, string language, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AchievementState>>([]);

        public Task<IReadOnlyList<AchievementChange>> ChangeAsync(uint appId, IReadOnlyList<string> apiNames, bool unlock, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AchievementChange>>([]);
    }
}
