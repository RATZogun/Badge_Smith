using CommunityToolkit.Mvvm.ComponentModel;

namespace BadgeSmith.Desktop.ViewModels;

public partial class FarmGameViewModel(uint appId, string name, bool blacklisted, Action<FarmGameViewModel> blacklistChanged) : ObservableObject
{
    [ObservableProperty]
    private int _remainingDrops;

    [ObservableProperty]
    private double _hoursPlayed;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isBlacklisted = blacklisted;

    [ObservableProperty]
    private bool _canChangeBlacklist = true;

    public uint AppId { get; } = appId;

    public string Name { get; } = name;

    partial void OnIsBlacklistedChanged(bool value) => blacklistChanged(this);
}
