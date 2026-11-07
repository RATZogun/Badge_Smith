namespace BadgeSmith.Core;

public enum AgentExitCode
{
    Stopped = 0,
    InvalidArguments = 2,
    SteamNotInstalled = 3,
    SteamNotRunning = 4,
    SteamNotLoggedOn = 5,
    WrongAppId = 6,
    UnsupportedSteam = 7,
    StatsUnavailable = 8,
    SchemaUnavailable = 9,
    StoreFailed = 10,
    GameNotAvailable = 11,
}

public static class AgentExitCodes
{
    public static bool IsSteamUnavailable(int exitCode) => (AgentExitCode)exitCode is
        AgentExitCode.SteamNotInstalled or
        AgentExitCode.SteamNotRunning or
        AgentExitCode.SteamNotLoggedOn or
        AgentExitCode.UnsupportedSteam;
}
