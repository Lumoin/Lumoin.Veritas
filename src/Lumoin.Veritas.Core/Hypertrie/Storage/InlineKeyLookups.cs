using System;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Implementations of <see cref="InlineKeyLookup"/>.
/// </summary>
/// <remarks>
/// <para>
/// Holds the scalar reference implementation and a factory that
/// selects the best available implementation for the current
/// hardware. Consumers do not need to be aware of which
/// implementation they receive — only that the delegate honours
/// the <see cref="InlineKeyLookup"/> contract.
/// </para>
/// </remarks>
public static class InlineKeyLookups
{
    /// <summary>
    /// The scalar reference implementation. Iterates the span
    /// linearly with branch-prediction friendly compares.
    /// </summary>
    public static InlineKeyLookup Scalar { get; } = ScalarImpl;

    /// <summary>
    /// Returns the best available implementation for the current
    /// hardware.
    /// </summary>
    /// <returns>The chosen <see cref="InlineKeyLookup"/> delegate.</returns>
    public static InlineKeyLookup SelectBestAvailable()
    {
        return Scalar;
    }

    private static int ScalarImpl(ReadOnlySpan<uint> keys, uint needle)
    {
        for(int i = 0; i < keys.Length; i++)
        {
            if(keys[i] == needle)
            {
                return i;
            }
        }

        return -1;
    }
}
