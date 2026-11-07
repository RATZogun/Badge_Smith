namespace BadgeSmith.Core;

public sealed record AgentStartResult(
    IReadOnlyList<uint> Started,
    IReadOnlyDictionary<uint, string> Failed,
    string? SteamUnavailable = null);

public interface IAgentLauncher
{
    Task<AgentStartResult> StartAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken);

    Task StopAllAsync();
}
