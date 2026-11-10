using System.Collections.ObjectModel;
using BadgeSmith.Core;
using BadgeSmith.Desktop.Services;
using BadgeSmith.Steam;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BadgeSmith.Desktop.ViewModels;

public enum FarmState
{
    Idle,
    Running,
    Paused,
}

public partial class FarmViewModel : ObservableObject, IDisposable
{
    private const int LogLimit = 200;
    private const string Waiting = "Na fila";
    private const string Ignored = "Ignorado pela lista";
    private const string Skipped = "Pulado";

    private readonly Func<CancellationToken, Task<IReadOnlyList<Badge>>> _scan;
    private readonly Func<AgentLauncher> _createLauncher;
    private readonly SettingsService _settings;
    private readonly Action<string>? _sessionExpired;
    private CancellationTokenSource? _cancel;
    private bool _stopRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsRunning), nameof(IsPaused))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(PauseCommand), nameof(StopCommand), nameof(RefreshCommand))]
    private FarmState _state;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(RefreshCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _summary = "Atualize a lista para ver os jogos com cartas a dropar.";

    public FarmViewModel(
        Func<CancellationToken, Task<IReadOnlyList<Badge>>> scan,
        Func<AgentLauncher> createLauncher,
        SettingsService settings,
        Action<string>? sessionExpired = null,
        string? unavailableReason = null)
    {
        _scan = scan;
        _createLauncher = createLauncher;
        _settings = settings;
        _sessionExpired = sessionExpired;
        UnavailableReason = unavailableReason;
        if (unavailableReason is not null)
        {
            Summary = unavailableReason;
        }
    }

    public string? UnavailableReason { get; }

    public bool IsAvailable => UnavailableReason is null;

    public ObservableCollection<FarmGameViewModel> Games { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public bool IsIdle => State == FarmState.Idle;

    public bool IsRunning => State == FarmState.Running;

    public bool IsPaused => State == FarmState.Paused;

    public void Dispose() => _cancel?.Cancel();

    public void Apply(FarmEvent farmEvent)
    {
        switch (farmEvent)
        {
            case FarmEvent.Scanned scanned:
                UpdateGames(scanned.WithDrops);
                Summary = $"{scanned.GamesWithDrops} jogo(s) com {scanned.TotalDrops} carta(s) a dropar.";
                break;
            case FarmEvent.Farming farming:
                var until = DateTime.Now + farming.Step.Duration;
                var mode = farming.Step.Mode == FarmMode.Solo ? "sozinho" : "em lote";
                foreach (var game in Games.Where(g => !g.IsBlacklisted && !g.Status.StartsWith(Skipped, StringComparison.Ordinal)))
                {
                    game.Status = farming.Started.Contains(game.AppId) ? $"Jogando {mode} até {until:HH:mm}" : Waiting;
                }

                Write($"Jogando {farming.Started.Count} jogo(s) {mode} por {farming.Step.Duration.TotalMinutes:0} min.");
                break;
            case FarmEvent.GameSkipped skipped:
                if (Find(skipped.AppId) is { } skippedGame)
                {
                    skippedGame.Status = $"{Skipped}: {Clean(skipped.Reason)}";
                }

                Write($"Jogo {Find(skipped.AppId)?.Name ?? skipped.AppId.ToString()} pulado: {Clean(skipped.Reason)}");
                break;
            case FarmEvent.SteamUnavailable unavailable:
                Summary = $"Farm pausado: {Clean(unavailable.Reason)} Nova tentativa em {unavailable.RetryIn.TotalSeconds:0} s.";
                Write(Summary);
                break;
            case FarmEvent.ScanFailed failed:
                Write($"Falha ao ler as badges ({failed.Error.Message}); nova tentativa em {failed.RetryIn.TotalSeconds:0} s.");
                break;
            case FarmEvent.Finished finished:
                Summary = finished.Skipped.Count == 0
                    ? "Todas as cartas foram dropadas."
                    : $"Fim: {finished.Skipped.Count} jogo(s) pulado(s) ainda têm cartas.";
                Write(Summary);
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsScanning = true;
        Summary = "Lendo as badges...";
        try
        {
            var badges = await _scan(CancellationToken.None);
            var withDrops = badges.Where(b => b.RemainingDrops > 0).ToList();
            UpdateGames(withDrops);
            Summary = PendingSummary();
        }
        catch (Exception error) when (error is SteamSessionExpiredException or SteamAuthException)
        {
            Summary = $"Não foi possível ler as badges: {error.Message}";
            _sessionExpired?.Invoke(error.Message);
        }
        catch (Exception error)
        {
            Summary = $"Não foi possível ler as badges: {error.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanRefresh() => IsAvailable && State == FarmState.Idle && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        _stopRequested = false;
        _cancel = new CancellationTokenSource();
        State = FarmState.Running;
        SetBlacklistEditable(false);
        foreach (var game in Games.Where(g => !g.IsBlacklisted && g.Status.StartsWith(Skipped, StringComparison.Ordinal)))
        {
            game.Status = Waiting;
        }

        Write("Farm iniciado.");
        var finished = false;
        try
        {
            await using var launcher = _createLauncher();
            var runner = new FarmRunner(_scan, launcher, _settings.Current.ToFarmOptions());
            await runner.RunAsync(e =>
            {
                Apply(e);
                finished |= e is FarmEvent.Finished;
            }, _cancel.Token);
        }
        catch (Exception error) when (error is SteamSessionExpiredException or SteamAuthException)
        {
            Summary = $"Farm parado: {error.Message}";
            Write(Summary);
            finished = true;
            _sessionExpired?.Invoke(error.Message);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Summary = $"Farm parado por um erro inesperado: {error.Message}";
            Write(Summary);
            finished = true;
        }
        finally
        {
            SetBlacklistEditable(true);
            if (finished || _stopRequested)
            {
                State = FarmState.Idle;
                ClearStatuses();
                if (_stopRequested)
                {
                    Summary = PendingSummary();
                }

                Write(_stopRequested ? "Farm parado." : "Farm encerrado.");
            }
            else
            {
                State = FarmState.Paused;
                foreach (var game in Games.Where(g => g.Status.StartsWith("Jogando", StringComparison.Ordinal)))
                {
                    game.Status = "Pausado";
                }

                Write("Farm pausado.");
            }
        }
    }

    private bool CanStart() => IsAvailable && State != FarmState.Running && !IsScanning;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Pause() => _cancel?.Cancel();

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _stopRequested = true;
        if (State == FarmState.Running)
        {
            _cancel?.Cancel();
        }
        else
        {
            State = FarmState.Idle;
            ClearStatuses();
            Summary = PendingSummary();
            Write("Farm parado.");
        }
    }

    private bool CanStop() => State != FarmState.Idle;

    private void UpdateGames(IReadOnlyList<Badge> badges)
    {
        var blacklist = _settings.Current.Blacklist.ToHashSet();
        var byId = badges.ToDictionary(b => b.AppId);
        foreach (var gone in Games.Where(g => !byId.ContainsKey(g.AppId)).ToList())
        {
            Games.Remove(gone);
        }

        foreach (var badge in badges.OrderByDescending(b => b.HoursPlayed).ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var game = Find(badge.AppId);
            if (game is null)
            {
                game = new FarmGameViewModel(badge.AppId, badge.Name, blacklist.Contains(badge.AppId), OnBlacklistChanged)
                {
                    CanChangeBlacklist = State != FarmState.Running,
                };
                game.Status = game.IsBlacklisted ? Ignored : Waiting;
                Games.Add(game);
            }

            game.RemainingDrops = badge.RemainingDrops;
            game.HoursPlayed = badge.HoursPlayed;
        }

        var ordered = Games.OrderByDescending(g => g.HoursPlayed).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = Games.IndexOf(ordered[i]);
            if (current != i)
            {
                Games.Move(current, i);
            }
        }
    }

    private void OnBlacklistChanged(FarmGameViewModel game)
    {
        if (game.Status is Waiting or Ignored or "")
        {
            game.Status = game.IsBlacklisted ? Ignored : Waiting;
        }

        _settings.Update(s => s with
        {
            Blacklist = game.IsBlacklisted
                ? [.. s.Blacklist.Append(game.AppId).Distinct()]
                : [.. s.Blacklist.Where(id => id != game.AppId)],
        });
    }

    private void SetBlacklistEditable(bool editable)
    {
        foreach (var game in Games)
        {
            game.CanChangeBlacklist = editable;
        }
    }

    private void ClearStatuses()
    {
        foreach (var game in Games)
        {
            game.Status = game.IsBlacklisted ? Ignored : Waiting;
        }
    }

    private string PendingSummary()
    {
        var pending = Games.Where(g => !g.IsBlacklisted).ToList();
        return $"{pending.Count} jogo(s) com {pending.Sum(g => g.RemainingDrops)} carta(s) a dropar.";
    }

    private FarmGameViewModel? Find(uint appId) => Games.FirstOrDefault(g => g.AppId == appId);

    private void Write(string line)
    {
        Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {line}");
        while (Log.Count > LogLimit)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }

    private static string Clean(string reason) => reason.Replace("ERRO ", "").Trim();
}
