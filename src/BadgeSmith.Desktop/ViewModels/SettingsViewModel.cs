using BadgeSmith.Core;
using BadgeSmith.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BadgeSmith.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    [ObservableProperty]
    private decimal? _hoursBeforeDrops;

    [ObservableProperty]
    private decimal? _maxConcurrentGames;

    [ObservableProperty]
    private int _themeIndex;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        _hoursBeforeDrops = (decimal)settings.Current.HoursBeforeDrops;
        _maxConcurrentGames = settings.Current.MaxConcurrentGames;
        _themeIndex = (int)settings.Current.Theme;
    }

    public IReadOnlyList<string> Themes { get; } = ["Do sistema", "Claro", "Escuro"];

    partial void OnHoursBeforeDropsChanged(decimal? value)
    {
        if (value is { } hours)
        {
            _settings.Update(s => s with { HoursBeforeDrops = (double)hours });
        }
    }

    partial void OnMaxConcurrentGamesChanged(decimal? value)
    {
        if (value is { } games)
        {
            _settings.Update(s => s with { MaxConcurrentGames = (int)games });
        }
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (Enum.IsDefined(typeof(AppTheme), value))
        {
            _settings.Update(s => s with { Theme = (AppTheme)value });
        }
    }
}
