namespace Lumoin.Veritas.Core.Columnar;

/// <summary>Which depth each relation of one Free Join run builds at.</summary>
internal enum FreeJoinDepthRule
{
    /// <summary>Every relation builds at its join-cover depth: trie levels through its last join variable in the global order, the private tail as leaf columns.</summary>
    JoinCover = 0,

    /// <summary>A relation whose cover key concentrates enough matches on one key value extends through its private tail; the rest keep their join-cover depth.</summary>
    FanOutEngaged = 1
}
