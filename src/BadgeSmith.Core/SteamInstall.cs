using Microsoft.Win32;

namespace BadgeSmith.Core;

public static class SteamInstall
{
    public static string? FindRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            return path is not null && Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
        ];
        return candidates.FirstOrDefault(c => Directory.Exists(Path.Combine(c, "appcache")));
    }

    public static bool IsRunning()
    {
        int pid;
        if (OperatingSystem.IsWindows())
        {
            pid = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "pid", 0) as int? ?? 0;
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var pidFile = Path.Combine(home, ".steam", "steam.pid");
            if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile).Trim(), out pid))
            {
                return false;
            }
        }

        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited && process.ProcessName.StartsWith("steam", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string? SchemaPath(string steamRoot, uint appId)
    {
        var path = Path.Combine(steamRoot, "appcache", "stats", $"UserGameStatsSchema_{appId}.bin");
        return File.Exists(path) ? path : null;
    }
}
