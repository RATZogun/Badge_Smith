using System.Text.Json;
using System.Text.Json.Serialization;

namespace BadgeSmith.Core;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed record AppSettings
{
    public double HoursBeforeDrops { get; init; } = 2;
    public int MaxConcurrentGames { get; init; } = 30;
    public IReadOnlyList<uint> Blacklist { get; init; } = [];
    public AppTheme Theme { get; init; } = AppTheme.System;
    public string? AccountName { get; init; }

    public FarmOptions ToFarmOptions() => new()
    {
        HoursBeforeDrops = Math.Max(0, HoursBeforeDrops),
        MaxConcurrentGames = Math.Clamp(MaxConcurrentGames, 1, 32),
        Blacklist = Blacklist.ToHashSet(),
    };
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SettingsStore ForCurrentUser()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        return new SettingsStore(Path.Combine(folder, "BadgeSmith", "settings.json"));
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings() : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
        File.Move(temporary, path, overwrite: true);
    }
}
