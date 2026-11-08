namespace BadgeSmith.Core;

public sealed record AchievementChangeOptions
{
    public TimeSpan MinDelay { get; init; } = TimeSpan.Zero;
    public TimeSpan MaxDelay { get; init; } = TimeSpan.Zero;

    public bool UsesDelay => MinDelay > TimeSpan.Zero || MaxDelay > TimeSpan.Zero;
}

public sealed class AchievementService(
    IAchievementAgent agent,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Random? random = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Random _random = random ?? Random.Shared;

    public Task<IReadOnlyList<AchievementState>> ListAsync(uint appId, string language, CancellationToken cancellationToken) =>
        agent.ListAsync(appId, language, cancellationToken);

    public async Task<IReadOnlyList<AchievementChange>> SetAsync(
        uint appId,
        IReadOnlyList<string> apiNames,
        bool unlock,
        AchievementChangeOptions options,
        IProgress<AchievementChange>? progress,
        CancellationToken cancellationToken)
    {
        var current = (await agent.ListAsync(appId, "english", cancellationToken)).ToDictionary(a => a.Api, StringComparer.Ordinal);
        var results = new List<AchievementChange>();
        var pending = new List<string>();

        foreach (var api in apiNames.Distinct(StringComparer.Ordinal))
        {
            AchievementChange? immediate = current.TryGetValue(api, out var state) switch
            {
                false => new AchievementChange(api, false, "conquista não existe neste jogo"),
                true when state!.Protected => new AchievementChange(api, false, "conquista protegida: só o jogo pode alterá-la"),
                true when state!.Achieved == unlock => new AchievementChange(api, true, null),
                _ => null,
            };

            if (immediate is null)
            {
                pending.Add(api);
            }
            else
            {
                results.Add(immediate);
                progress?.Report(immediate);
            }
        }

        if (pending.Count == 0)
        {
            return results;
        }

        if (!options.UsesDelay)
        {
            var changed = await agent.ChangeAsync(appId, pending, unlock, cancellationToken);
            foreach (var change in changed)
            {
                results.Add(change);
                progress?.Report(change);
            }

            return results;
        }

        for (var i = 0; i < pending.Count; i++)
        {
            if (i > 0)
            {
                await _delay(NextDelay(options), cancellationToken);
            }

            foreach (var change in await agent.ChangeAsync(appId, [pending[i]], unlock, cancellationToken))
            {
                results.Add(change);
                progress?.Report(change);
            }
        }

        return results;
    }

    private TimeSpan NextDelay(AchievementChangeOptions options)
    {
        var min = options.MinDelay.Ticks;
        var max = Math.Max(options.MinDelay.Ticks, options.MaxDelay.Ticks);
        return TimeSpan.FromTicks(min + (long)(_random.NextDouble() * (max - min)));
    }
}
