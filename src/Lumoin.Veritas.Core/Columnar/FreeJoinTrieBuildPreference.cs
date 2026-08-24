namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// How a join-route decision names the Free Join route's tries should materialise their maps.
/// </summary>
public enum FreeJoinTrieBuildPreference
{
    /// <summary>No build mode was decided, so <see cref="QueryEnginePolicy.FreeJoinTrieBuild"/> applies. The default.</summary>
    Unspecified = 0,

    /// <summary>The whole trie hashes at build time.</summary>
    Eager = 1,

    /// <summary>Each map materialises on its first navigation touch, over a retained column store.</summary>
    Lazy = 2
}
