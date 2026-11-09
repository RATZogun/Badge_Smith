using BadgeSmith.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BadgeSmith.Desktop.ViewModels;

public partial class AchievementItemViewModel(AchievementState state) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private bool _isChecked = state.Achieved == true;

    public string Api { get; } = state.Api;

    public string Name { get; } = state.Name.Length > 0 ? state.Name : state.Api;

    public string Description { get; } = state.Hidden && state.Description.Length == 0 ? "Conquista oculta" : state.Description;

    public bool IsProtected { get; } = state.Protected;

    public bool CanChange => !IsProtected;

    public bool IsAchieved { get; } = state.Achieved == true;

    public bool IsChanged => IsChecked != IsAchieved;

    public string Status { get; } = state switch
    {
        { Protected: true } => "Protegida: só o jogo pode alterar",
        { Achieved: true, UnlockTime: { } time } => $"Desbloqueada em {time.ToLocalTime():dd/MM/yyyy HH:mm}",
        { Achieved: true } => "Desbloqueada",
        _ => "Bloqueada",
    };
}
