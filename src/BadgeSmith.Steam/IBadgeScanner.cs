using BadgeSmith.Core;

namespace BadgeSmith.Steam;

public interface IBadgeScanner : IDisposable
{
    Task<IReadOnlyList<Badge>> ScanAsync(CancellationToken cancellationToken = default);
}
