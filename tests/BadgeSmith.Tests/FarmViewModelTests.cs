using BadgeSmith.Core;
using BadgeSmith.Desktop.Services;
using BadgeSmith.Desktop.ViewModels;

namespace BadgeSmith.Tests;

public sealed class FarmViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"badgesmith-{Guid.NewGuid():N}");
    private readonly SettingsService _settings;

    public FarmViewModelTests()
    {
        _settings = new SettingsService(new SettingsStore(Path.Combine(_folder, "settings.json")));
        _settings.Update(s => s with { Blacklist = [30] });
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void ListsTheScannedGamesByHoursWithTheBlacklistMarked()
    {
        var farm = NewFarm();
        Badge[] withDrops = [new(10, "Pouco jogado", 3, 0.5), new(20, "Mais jogado", 2, 4), new(30, "Ignorado", 1, 1)];

        farm.Apply(new FarmEvent.Scanned(withDrops, withDrops.Where(b => b.AppId != 30).ToList()));

        Assert.Equal([20u, 30u, 10u], farm.Games.Select(g => g.AppId));
        Assert.True(farm.Games.Single(g => g.AppId == 30).IsBlacklisted);
        Assert.Equal("Ignorado pela lista", farm.Games.Single(g => g.AppId == 30).Status);
        Assert.Equal("2 jogo(s) com 5 carta(s) a dropar.", farm.Summary);
    }

    [Fact]
    public void ShowsWhichGamesArePlayingAndWhichWereSkipped()
    {
        var farm = NewFarm();
        Badge[] withDrops = [new(10, "A", 3, 0.5), new(20, "B", 2, 0.4), new(40, "C", 1, 0.1)];
        farm.Apply(new FarmEvent.Scanned(withDrops, withDrops));

        farm.Apply(new FarmEvent.GameSkipped(40, "ERRO a Steam recusou o jogo 40"));
        farm.Apply(new FarmEvent.Farming(new FarmStep(FarmMode.Batch, withDrops, TimeSpan.FromMinutes(6)), [10]));

        Assert.StartsWith("Jogando em lote até", farm.Games.Single(g => g.AppId == 10).Status);
        Assert.Equal("Na fila", farm.Games.Single(g => g.AppId == 20).Status);
        Assert.Equal("Pulado: a Steam recusou o jogo 40", farm.Games.Single(g => g.AppId == 40).Status);
    }

    [Fact]
    public void SavesTheBlacklistWhenAGameIsToggled()
    {
        var farm = NewFarm();
        Badge[] withDrops = [new(10, "A", 3, 0.5), new(30, "B", 1, 1)];
        farm.Apply(new FarmEvent.Scanned(withDrops, withDrops));

        farm.Games.Single(g => g.AppId == 10).IsBlacklisted = true;
        farm.Games.Single(g => g.AppId == 30).IsBlacklisted = false;

        Assert.Equal([10u], _settings.Current.Blacklist);
        Assert.Equal("Ignorado pela lista", farm.Games.Single(g => g.AppId == 10).Status);
    }

    private FarmViewModel NewFarm() =>
        new(_ => Task.FromResult<IReadOnlyList<Badge>>([]), () => throw new InvalidOperationException("não deveria iniciar agentes"), _settings);
}
