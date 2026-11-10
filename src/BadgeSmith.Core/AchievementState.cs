namespace BadgeSmith.Core;

public sealed record AchievementState(
    string Api,
    string Name,
    string Description,
    bool Hidden,
    bool Protected,
    bool? Achieved,
    DateTimeOffset? UnlockTime,
    string? Icon = null,
    string? IconGray = null);

public sealed record AchievementChange(string Api, bool Ok, string? Error);

public sealed class AchievementAgentException(AgentExitCode code, string message) : Exception(message)
{
    public AgentExitCode Code { get; } = code;
}

public interface IAchievementAgent
{
    Task<IReadOnlyList<AchievementState>> ListAsync(uint appId, string language, CancellationToken cancellationToken);

    Task<IReadOnlyList<AchievementChange>> ChangeAsync(uint appId, IReadOnlyList<string> apiNames, bool unlock, CancellationToken cancellationToken);
}
