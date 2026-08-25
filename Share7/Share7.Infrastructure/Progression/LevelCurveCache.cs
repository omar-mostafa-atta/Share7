using Share7.Application.Progression.Interfaces;
using Share7.Domain.Progression;

namespace Share7.Infrastructure.Progression;

/// <inheritdoc cref="ILevelCurveCache"/>
public class LevelCurveCache : ILevelCurveCache
{
    // A reference swap, not a mutation. Readers on other threads see either the whole previous curve
    // or the whole new one, never a list being rebuilt underneath them — which is what a lock here
    // would otherwise be for, on a read that happens on every attempt.
    private volatile IReadOnlyList<LevelThreshold>? _curve;

    public IReadOnlyList<LevelThreshold>? Current => _curve;

    public void Set(IReadOnlyList<LevelThreshold> curve) => _curve = curve;

    public void Invalidate() => _curve = null;
}
