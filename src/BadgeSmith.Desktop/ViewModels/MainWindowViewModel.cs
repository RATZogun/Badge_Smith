using BadgeSmith.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BadgeSmith.Desktop.ViewModels;

public partial class MainWindowViewModel(SessionService session, SettingsService settings, string agentPath) : ObservableObject
{
    [ObservableProperty]
    private ObservableObject? _current;

    public Task StartAsync()
    {
        try
        {
            if (session.Restore())
            {
                ShowShell();
            }
            else
            {
                ShowLogin();
            }
        }
        catch (Steam.SessionVaultException error)
        {
            Current = new LoginViewModel(session, OnLoggedIn, error.Message, startQr: false);
        }

        return Task.CompletedTask;
    }

    public void Shutdown()
    {
        if (Current is IDisposable disposable)
        {
            disposable.Dispose();
        }

        session.Dispose();
    }

    private void ShowLogin() => Current = new LoginViewModel(session, OnLoggedIn);

    private void OnLoggedIn(string accountName)
    {
        if (Current is IDisposable disposable)
        {
            disposable.Dispose();
        }

        settings.Update(s => s with { AccountName = accountName });
        ShowShell();
    }

    private void ShowShell() => Current = new ShellViewModel(session, settings, agentPath, LogoutAsync, OnSessionExpired);

    private void OnSessionExpired(string reason)
    {
        if (Current is not ShellViewModel shell)
        {
            return;
        }

        shell.Dispose();
        Current = new LoginViewModel(session, OnLoggedIn, $"{reason} Entre de novo.");
    }

    private async Task LogoutAsync()
    {
        if (Current is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await session.LogoutAsync();
        settings.Update(s => s with { AccountName = null });
        ShowLogin();
    }
}
