namespace BadgeSmith.Core;

public sealed record AgentStartResult(IReadOnlyList<uint> Started, IReadOnlyDictionary<uint, string> Failed);

public interface IAgentLauncher
{
    Task<AgentStartResult> StartAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken);

    Task StopAllAsync();
}
