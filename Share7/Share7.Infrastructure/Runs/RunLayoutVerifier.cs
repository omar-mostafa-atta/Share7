using Share7.Application.Runs.Interfaces;

namespace Share7.Infrastructure.Runs;

/// <summary>
/// Resolves layout generators by game and version, from whatever is registered in DI.
/// <para>
/// <b>Empty by default, and that is the shipped state.</b> No <see cref="IRunLayoutGenerator"/> is
/// registered for any game yet, because porting one is a port of the Unity track generator and a
/// half-ported generator is strictly worse than none: it would reject real runs from real children
/// while looking like it was working. Registering one is a single <c>AddSingleton</c>, and the
/// verification path in <c>RunService</c> is already written and tested against a reference
/// implementation — see <c>RunLayoutVerificationTests</c>.
/// </para>
/// <para>
/// Several versions of one game may be registered at once, and normally are: a staged rollout has two
/// client builds live, each generating with its own version, and both have to verify. Retiring a
/// version means unregistering it once nothing can still be carrying it — a run stamped with a version
/// this deployment no longer has settles unverified rather than failing, so an early retirement costs
/// a defence rather than a child's run.
/// </para>
/// </summary>
public class RunLayoutVerifier : IRunLayoutVerifier
{
    private readonly Dictionary<(string GameKey, int Version), IRunLayoutGenerator> _byVersion;
    private readonly Dictionary<string, int> _currentByGame;

    public RunLayoutVerifier(IEnumerable<IRunLayoutGenerator> generators)
    {
        var all = generators.Where(g => g.Version > 0).ToList();

        _byVersion = all.ToDictionary(g => (g.GameKey, g.Version));

        // The newest registered version is what a new run is stamped with. Older ones stay resolvable
        // for runs already carrying them; they simply stop being issued.
        _currentByGame = all
            .GroupBy(g => g.GameKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Version), StringComparer.Ordinal);
    }

    public int VersionFor(string gameKey) => _currentByGame.GetValueOrDefault(gameKey);

    public RunLayout? Derive(string gameKey, int version, long seed) =>
        version > 0 && _byVersion.TryGetValue((gameKey, version), out var generator)
            ? generator.Generate(seed)
            : null;
}
