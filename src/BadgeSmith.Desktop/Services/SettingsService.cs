using BadgeSmith.Core;

namespace BadgeSmith.Desktop.Services;

public sealed class SettingsService(SettingsStore store)
{
    public AppSettings Current { get; private set; } = store.Load();

    public event Action<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        store.Save(Current);
        Changed?.Invoke(Current);
    }
}
