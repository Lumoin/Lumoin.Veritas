using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Inline storage for up to 8 <see cref="HypertrieNode"/> child
/// references, used by <see cref="EdgeMap"/>'s Inline tier. Stored
/// directly inside the <see cref="EdgeMap"/> struct; no heap
/// allocation for the buffer itself. The capacity matches
/// <see cref="EdgeMap.InlineCapacity"/>; the literal is repeated
/// here because <see cref="InlineArrayAttribute"/> requires a
/// compile-time constant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nullable slot semantics.</b> The element type is
/// <see cref="HypertrieNode"/>? rather than <see cref="HypertrieNode"/>
/// so depth-1 leaves — where the edge map's keys alone are the
/// answer and the child slot is structurally absent — can store
/// <c>null</c>. Depth-2 and depth-3 EdgeMap slots hold real
/// (non-<c>null</c>) child references.
/// </para>
/// </remarks>
[InlineArray(8)]
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "InlineChildBuffer is a storage cell exposed through ref-indexer access, not a value-compared type. Consumers always access elements positionally via the implicit indexer; comparing two whole buffers for equality is not a meaningful operation.")]
public struct InlineChildBuffer
{
    private HypertrieNode? element;
}
