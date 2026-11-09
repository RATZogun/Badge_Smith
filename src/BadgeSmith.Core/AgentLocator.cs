namespace BadgeSmith.Core;

public static class AgentLocator
{
    public static string DefaultPath()
    {
        var name = OperatingSystem.IsWindows() ? "BadgeSmith.Agent.exe" : "BadgeSmith.Agent";
        var sibling = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(sibling))
        {
            return sibling;
        }

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BadgeSmith.Agent", "bin", configuration, "net10.0", name));
    }
}
