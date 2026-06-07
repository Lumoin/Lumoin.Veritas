using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The packed equi-join key of a batched hash join: one or two
/// encoded join values folded into a single 64-bit value, with the
/// bit layout owned in one place. A named type so a key cannot be
/// confused with a row id, a triple count, or a single term value,
/// and so the one or two-variable packing convention lives here
/// rather than scattered across the join.
/// </summary>
/// <remarks>
/// The first value occupies the high 32 bits, the second the low 32
/// — single-variable joins pass zero for the second, so distinct
/// single values never collide. Value equality and hashing come
/// from the record struct, so a <c>Dictionary&lt;JoinKey, …&gt;</c>
/// keys on the packed bits directly.
/// </remarks>
[DebuggerDisplay("JoinKey {Value:X16}")]
public readonly record struct JoinKey(ulong Value)
{
    /// <summary>Packs one or two encoded join values into a key; the second is zero for a single-variable join.</summary>
    /// <param name="first">The first encoded join value.</param>
    /// <param name="second">The second encoded join value, or zero.</param>
    /// <returns>The packed key.</returns>
    public static JoinKey Pack(uint first, uint second)
    {
        return new JoinKey(((ulong)first << 32) | second);
    }
}
