namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// How a <see cref="GeneralizedHashTrie"/> materialises its internal maps.
/// Both modes serve the same navigation contract and yield the same answers;
/// they trade where the hashing work and the retained memory land. Eager is
/// the measured default; lazy stays selectable where leaving never-descended
/// subtries unbuilt outweighs retaining the column store.
/// </summary>
public enum FreeJoinTrieBuild
{
    /// <summary>
    /// Every internal map is hashed at build time and the leaf tuples are
    /// copied into packed vectors; the built trie is read-only. The whole
    /// relation pays the hashing up front, touched or not.
    /// </summary>
    Eager,

    /// <summary>
    /// The column-oriented lazy trie: the build stores the relation's columns
    /// and the root row set without hashing, each internal map materialises on
    /// its first navigation touch, and leaves reference rows in the column
    /// store instead of copying tuples. Keys the join never descends leave
    /// their subtries unbuilt, at the price of retaining the column store for
    /// the trie's lifetime; navigation mutates the trie, so a lazy trie serves
    /// one query's descent on one thread.
    /// </summary>
    Lazy
}
