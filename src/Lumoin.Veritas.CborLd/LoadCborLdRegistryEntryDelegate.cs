using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Resolves a CBOR-LD registry entry identifier to its
/// <see cref="CborLdRegistryEntry"/>. Mirrors the
/// <c>ContextResolverDelegate</c> pattern: a single delegate that the
/// caller wires up to a local cache, an embedded registry set, or a
/// remote registry service.
/// </summary>
/// <remarks>
/// <para>
/// Returning a <c>null</c> entry indicates the requested identifier is
/// not known; the caller will surface this as a
/// <see cref="CborLdProcessingException"/> with a missing-entry error
/// code at decode time.
/// </para>
/// <para>
/// Registry entry <c>0</c> is always the passthrough entry
/// (<see cref="CborLdRegistryEntry.Passthrough"/>); implementations may
/// short-circuit for that id rather than going through a lookup.
/// </para>
/// </remarks>
/// <param name="registryEntryId">The identifier to resolve.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
/// <returns>The resolved entry, or <c>null</c> when the id is not registered.</returns>
public delegate ValueTask<CborLdRegistryEntry?> LoadCborLdRegistryEntryDelegate(
    int registryEntryId,
    CancellationToken cancellationToken);
