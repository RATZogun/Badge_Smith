using Avalonia.Media.Imaging;
using BadgeSmith.Steam;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BadgeSmith.Desktop.ViewModels;

public partial class GameItemViewModel(OwnedGame game) : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText), nameof(HasProgress))]
    private AchievementProgress? _progress;

    public uint AppId { get; } = game.AppId;

    public string Name { get; } = game.Name;

    public double HoursPlayed { get; } = game.HoursPlayed;

    public string? IconHash { get; } = game.IconHash;

    public bool HasProgress => Progress is not null;

    public string? ProgressText => Progress is { } progress ? $"{progress.Unlocked}/{progress.Total}" : null;
}
