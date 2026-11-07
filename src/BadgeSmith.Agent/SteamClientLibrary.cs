using Microsoft.Win32;

namespace BadgeSmith.Agent;

internal static class SteamClientLibrary
{
    public static string? FindPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (steamPath is null)
            {
                return null;
            }

            var file = Path.Combine(steamPath, Environment.Is64BitProcess ? "steamclient64.dll" : "steamclient.dll");
            return File.Exists(file) ? file : null;
        }

        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates =
            [
                Path.Combine(home, ".steam", "sdk64", "steamclient.so"),
                Path.Combine(home, ".local", "share", "Steam", "linux64", "steamclient.so"),
                Path.Combine(home, ".steam", "steam", "linux64", "steamclient.so"),
            ];
            return candidates.FirstOrDefault(File.Exists);
        }

        return null;
    }
}
