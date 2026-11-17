using BadgeSmith.Core;

namespace BadgeSmith.Steam;

public interface IBadgeScanner : IDisposable
{
    bool? AccountLimited { get; }

    Task<IReadOnlyList<Badge>> ScanAsync(CancellationToken cancellationToken = default);
}
