using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using BadgeSmith.Core;
using BadgeSmith.Desktop.Services;
using BadgeSmith.Desktop.ViewModels;
using BadgeSmith.Desktop.Views;

namespace BadgeSmith.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService(SettingsStore.ForCurrentUser());
            ApplyTheme(settings.Current.Theme);
            settings.Changed += s => ApplyTheme(s.Theme);

            var main = new MainWindowViewModel(new SessionService(), settings, AgentLocator.DefaultPath());
            desktop.MainWindow = new MainWindow { DataContext = main };
            desktop.ShutdownRequested += (_, _) => main.Shutdown();
            _ = main.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
