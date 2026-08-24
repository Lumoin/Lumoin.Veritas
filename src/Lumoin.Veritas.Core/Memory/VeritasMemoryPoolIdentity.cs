using System.Threading;

namespace Lumoin.Veritas.Core.Memory;

/// <summary>
/// Mints process-unique instance identities for <see cref="VeritasMemoryPool{T}"/> instances.
/// One counter serves every generic instantiation: instruments from pools of different element
/// types share the same instrument names, so an identity unique only per element type would
/// collide across types and break per-instance measurement attribution.
/// </summary>
internal static class VeritasMemoryPoolIdentity
{
    /// <summary>The last minted identity; zero before any pool exists.</summary>
    private static int lastIdentity;

    /// <summary>Mints the next process-unique pool instance identity.</summary>
    /// <returns>A positive identity no other pool instance in this process carries.</returns>
    public static int Next()
    {
        return Interlocked.Increment(ref lastIdentity);
    }
}
