using BadgeSmith.Core;

namespace BadgeSmith.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"badgesmith-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void SavesAndLoadsTheSettings()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var settings = new AppSettings { HoursBeforeDrops = 1.5, MaxConcurrentGames = 10, Blacklist = [440, 570], Theme = AppTheme.Dark, AccountName = "conta" };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal((1.5, 10, AppTheme.Dark, "conta"), (loaded.HoursBeforeDrops, loaded.MaxConcurrentGames, loaded.Theme, loaded.AccountName));
        Assert.Equal([440u, 570u], loaded.Blacklist);
    }

    [Fact]
    public void FallsBackToTheDefaultsWhenTheFileIsMissingOrBroken()
    {
        var path = Path.Combine(_folder, "settings.json");
        var store = new SettingsStore(path);
        Assert.Equal(new AppSettings().HoursBeforeDrops, store.Load().HoursBeforeDrops);

        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, "{ quebrado");
        Assert.Equal(AppTheme.System, store.Load().Theme);
    }

    [Fact]
    public void KeepsTheFarmOptionsInsideSteamLimits()
    {
        var options = new AppSettings { HoursBeforeDrops = -1, MaxConcurrentGames = 100, Blacklist = [10] }.ToFarmOptions();

        Assert.Equal((0d, 32), (options.HoursBeforeDrops, options.MaxConcurrentGames));
        Assert.Contains(10u, options.Blacklist);
    }
}
