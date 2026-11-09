using Avalonia.Media.Imaging;
using BadgeSmith.Desktop.Services;
using BadgeSmith.Steam;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;

namespace BadgeSmith.Desktop.ViewModels;

public partial class LoginViewModel : ObservableObject, IDisposable
{
    private static readonly int[] ExpiredResults = [9, 27];

    private readonly SessionService _session;
    private readonly Action<string> _loggedIn;
    private CancellationTokenSource _cancel = new();

    [ObservableProperty]
    private Bitmap? _qrCode;

    [ObservableProperty]
    private bool _useCookie;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInWithCookieCommand))]
    private string _cookie = "";

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInWithCookieCommand))]
    private bool _isBusy;

    public LoginViewModel(SessionService session, Action<string> loggedIn, string? message = null, bool startQr = true)
    {
        _session = session;
        _loggedIn = loggedIn;
        _message = message;
        if (startQr)
        {
            _ = RunQrAsync(_cancel.Token);
        }
    }

    public void Dispose() => _cancel.Cancel();

    partial void OnUseCookieChanged(bool value)
    {
        _cancel.Cancel();
        _cancel = new CancellationTokenSource();
        IsBusy = false;
        if (!value)
        {
            _ = RunQrAsync(_cancel.Token);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSignInWithCookie))]
    private async Task SignInWithCookieAsync()
    {
        IsBusy = true;
        Message = null;
        try
        {
            var session = await _session.SaveCookieAsync(Cookie, _cancel.Token);
            _loggedIn(session.SteamId.ToString());
        }
        catch (ArgumentException error)
        {
            Message = error.Message;
        }
        catch (SteamSessionExpiredException)
        {
            Message = "A Steam não aceitou esse cookie: ele expirou ou é de uma sessão encerrada. Copie um novo.";
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !_cancel.IsCancellationRequested)
        {
            Message = $"Não foi possível falar com a Steam ({error.Message}).";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSignInWithCookie() => !IsBusy && Cookie.Trim().Length > 0;

    private async Task RunQrAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var login = new SteamLogin(_session.Auth);
                var result = await login.WithQrAsync(url => QrCode = Render(url), cancellationToken);
                await _session.SaveAsync(result.RefreshToken, cancellationToken);
                _loggedIn(result.AccountName);
                return;
            }
            catch (SteamAuthException error) when (ExpiredResults.Contains(error.Result))
            {
            }
            catch (SteamAuthException error)
            {
                QrCode = null;
                Message = error.Message;
                return;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                QrCode = null;
                Message = $"Não foi possível falar com a Steam ({error.Message}). Tentando de novo...";
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static Bitmap Render(string url)
    {
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.L);
        using var stream = new MemoryStream(new PngByteQRCode(data).GetGraphic(8));
        return new Bitmap(stream);
    }
}
