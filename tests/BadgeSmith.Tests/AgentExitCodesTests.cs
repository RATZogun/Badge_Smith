using BadgeSmith.Core;

namespace BadgeSmith.Tests;

public class AgentExitCodesTests
{
    [Theory]
    [InlineData(AgentExitCode.SteamNotInstalled, true)]
    [InlineData(AgentExitCode.SteamNotRunning, true)]
    [InlineData(AgentExitCode.SteamNotLoggedOn, true)]
    [InlineData(AgentExitCode.UnsupportedSteam, true)]
    [InlineData(AgentExitCode.WrongAccount, true)]
    [InlineData(AgentExitCode.WrongAppId, false)]
    [InlineData(AgentExitCode.GameNotAvailable, false)]
    [InlineData(AgentExitCode.StatsUnavailable, false)]
    public void PausesOnlyForSteamProblems(AgentExitCode code, bool steamUnavailable)
    {
        Assert.Equal(steamUnavailable, AgentExitCodes.IsSteamUnavailable((int)code));
    }
}
