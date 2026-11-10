using BadgeSmith.Core;
using BadgeSmith.Desktop.Services;
using BadgeSmith.Steam;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BadgeSmith.Desktop.ViewModels;

public partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly SessionService _session;
    private readonly string _agentPath;
    private readonly RenewingBadgeScanner _scanner;
    private readonly OwnedGamesClient _ownedGames = new();
    private readonly PosterService _posters = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly Func<Task> _logout;

    [ObservableProperty]
    private string? _accountWarning;

    public ShellViewModel(SessionService session, SettingsService settings, string agentPath, Func<Task> logout, Action<string> sessionExpired)
    {
        _session = session;
        _agentPath = agentPath;
        _logout = logout;
        _scanner = new RenewingBadgeScanner(session.OpenAsync);
        AccountName = settings.Current.AccountName is { Length: > 0 } name ? name : session.SteamId?.ToString() ?? "";

        Farm = new FarmViewModel(
            _scanner.ScanAsync,
            () => new AgentLauncher(agentPath, session.SteamId),
            settings,
            sessionExpired,
            session.CanReadBadges
                ? null
                : "O cookie usado no login é da loja e não lê as badges da comunidade. Para farmar cartas, saia e entre com o steamLoginSecure copiado com steamcommunity.com aberto. As conquistas funcionam normalmente.",
            () => _scanner.AccountLimited,
            _posters.LoadAsync);
        Achievements = new AchievementsViewModel(
            async ct => await _ownedGames.GetAsync(await session.OpenAsync(ct), ct),
            new AchievementService(new AgentAchievementClient(agentPath, session.SteamId)),
            sessionExpired,
            async ids => await _ownedGames.GetAchievementProgressAsync(await session.OpenAsync(CancellationToken.None), ids, CancellationToken.None),
            _posters.LoadIconsAsync);
        Settings = new SettingsViewModel(settings);

        _ = CheckAccountAsync();
        if (Farm.UnavailableReason is null)
        {
            Farm.RefreshCommand.Execute(null);
        }
        Achievements.LoadGamesCommand.Execute(null);
    }

    public string AccountName { get; }

    public FarmViewModel Farm { get; }

    public AchievementsViewModel Achievements { get; }

    public SettingsViewModel Settings { get; }

    public void Dispose()
    {
        _cancel.Cancel();
        Farm.Dispose();
        _scanner.Dispose();
        _ownedGames.Dispose();
        _posters.Dispose();
    }

    [RelayCommand]
    private Task LogoutAsync() => _logout();

    [RelayCommand]
    private Task RecheckAccountAsync() => CheckAccountAsync();

    private async Task CheckAccountAsync()
    {
        if (!File.Exists(_agentPath))
        {
            AccountWarning = $"Agente não encontrado em {_agentPath}. Compile o BadgeSmith.Agent.";
            return;
        }

        try
        {
            await using var launcher = new AgentLauncher(_agentPath, _session.SteamId);
            AccountWarning = await launcher.CheckAccountAsync(_cancel.Token) is { } mismatch
                ? $"{Capitalize(mismatch.Replace("ERRO ", ""))} Entre na Steam com a conta {AccountName} para farmar e alterar conquistas."
                : null;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
