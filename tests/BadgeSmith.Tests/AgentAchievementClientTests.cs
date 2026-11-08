using BadgeSmith.Core;

namespace BadgeSmith.Tests;

public class AgentAchievementClientTests
{
    [Fact]
    public async Task ReportsAMissingAgentAsAnAgentError()
    {
        var client = new AgentAchievementClient(Path.Combine(Path.GetTempPath(), "badgesmith-sem-agente"));

        var error = await Assert.ThrowsAsync<AchievementAgentException>(() => client.ListAsync(1, "english", CancellationToken.None));

        Assert.Equal(AgentExitCode.AgentNotStarted, error.Code);
    }
}
