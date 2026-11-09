using System.Collections.ObjectModel;
using System.Globalization;
using BadgeSmith.Core;
using BadgeSmith.Steam;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BadgeSmith.Desktop.ViewModels;

public partial class AchievementsViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<OwnedGame>>> _loadGames;
    private readonly AchievementService _service;
    private readonly Action<string>? _sessionExpired;
    private IReadOnlyList<OwnedGame> _allGames = [];
    private CancellationTokenSource? _loading;

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGame))]
    private OwnedGame? _selectedGame;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(ReloadCommand), nameof(LoadGamesCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string _gamesSummary = "";

    public AchievementsViewModel(
        Func<CancellationToken, Task<IReadOnlyList<OwnedGame>>> loadGames,
        AchievementService service,
        Action<string>? sessionExpired = null)
    {
        _loadGames = loadGames;
        _service = service;
        _sessionExpired = sessionExpired;
    }

    public ObservableCollection<OwnedGame> Games { get; } = [];

    public ObservableCollection<AchievementItemViewModel> Achievements { get; } = [];

    public bool HasGame => SelectedGame is not null;

    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "brazilian" : "english";

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LoadGamesAsync()
    {
        IsBusy = true;
        GamesSummary = "Carregando a biblioteca...";
        try
        {
            _allGames = (await _loadGames(CancellationToken.None)).Where(g => g.HasAchievements).ToList();
            ApplyFilter();
        }
        catch (Exception error) when (error is SteamSessionExpiredException or SteamAuthException)
        {
            GamesSummary = $"Não foi possível carregar a biblioteca: {error.Message}";
            _sessionExpired?.Invoke(error.Message);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            GamesSummary = $"Não foi possível carregar a biblioteca: {error.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedGameChanged(OwnedGame? value)
    {
        Achievements.Clear();
        Message = null;
        if (value is not null)
        {
            _ = LoadAchievementsAsync(value);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ReloadAsync() => SelectedGame is { } game ? LoadAchievementsAsync(game) : Task.CompletedTask;

    [RelayCommand]
    private void CheckAll() => SetAll(true);

    [RelayCommand]
    private void UncheckAll() => SetAll(false);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SaveAsync()
    {
        if (SelectedGame is not { } game)
        {
            return;
        }

        var unlock = Achievements.Where(a => a.IsChanged && a.IsChecked).Select(a => a.Api).ToList();
        var relock = Achievements.Where(a => a.IsChanged && !a.IsChecked).Select(a => a.Api).ToList();
        if (unlock.Count + relock.Count == 0)
        {
            Message = "Nenhuma alteração para gravar.";
            return;
        }

        IsBusy = true;
        Message = "Gravando na Steam...";
        var results = new List<AchievementChange>();
        try
        {
            var options = new AchievementChangeOptions();
            if (unlock.Count > 0)
            {
                results.AddRange(await _service.SetAsync(game.AppId, unlock, unlock: true, options, null, CancellationToken.None));
            }

            if (relock.Count > 0)
            {
                results.AddRange(await _service.SetAsync(game.AppId, relock, unlock: false, options, null, CancellationToken.None));
            }

            var failed = results.Where(r => !r.Ok).ToList();
            Message = failed.Count == 0
                ? $"{results.Count} conquista(s) gravada(s)."
                : $"{results.Count - failed.Count} gravada(s), {failed.Count} com falha: {string.Join("; ", failed.Select(f => $"{f.Api} ({f.Error})"))}";
        }
        catch (AchievementAgentException error)
        {
            Message = Describe(error);
        }
        finally
        {
            IsBusy = false;
        }

        var saved = Message;
        await LoadAchievementsAsync(game);
        Message ??= saved;
    }

    private bool IsIdle() => !IsBusy;

    private async Task LoadAchievementsAsync(OwnedGame game)
    {
        _loading?.Cancel();
        var loading = _loading = new CancellationTokenSource();
        IsBusy = true;
        Message = null;
        try
        {
            var achievements = await _service.ListAsync(game.AppId, Language, loading.Token);
            if (loading.IsCancellationRequested || SelectedGame != game)
            {
                return;
            }

            Achievements.Clear();
            foreach (var achievement in achievements)
            {
                Achievements.Add(new AchievementItemViewModel(achievement));
            }

            if (Achievements.Count == 0)
            {
                Message = "Este jogo não tem conquistas.";
            }
        }
        catch (AchievementAgentException error)
        {
            if (SelectedGame == game)
            {
                Message = Describe(error);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_loading == loading)
            {
                IsBusy = false;
            }
        }
    }

    private void SetAll(bool value)
    {
        foreach (var achievement in Achievements.Where(a => a.CanChange))
        {
            achievement.IsChecked = value;
        }
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        var selected = SelectedGame;
        Games.Clear();
        if (uint.TryParse(filter, out var appId) && appId > 0 && _allGames.All(g => g.AppId != appId))
        {
            Games.Add(selected?.AppId == appId ? selected : new OwnedGame(appId, $"App {appId}", 0, true));
        }

        foreach (var game in _allGames.Where(g => filter.Length == 0 || g.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) || g.AppId.ToString() == filter))
        {
            Games.Add(game);
        }

        if (selected is not null && Games.Contains(selected))
        {
            SelectedGame = selected;
        }

        GamesSummary = $"{Games.Count} de {_allGames.Count} jogo(s) com conquistas";
    }

    private static string Describe(AchievementAgentException error) => error.Code switch
    {
        AgentExitCode.WrongAccount => $"{error.Message.Replace("ERRO ", "")} Entre na Steam com a conta do Badge Smith.",
        AgentExitCode.GameNotAvailable => "A Steam recusou o jogo: ele não está na biblioteca da conta logada no cliente da Steam.",
        AgentExitCode.SteamNotRunning or AgentExitCode.SteamNotLoggedOn => "Abra a Steam e entre na sua conta.",
        _ => error.Message.Replace("ERRO ", ""),
    };
}
