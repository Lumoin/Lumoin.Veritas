namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Caps the SIMD codec backend ladder <em>below</em> the machine's
/// detected hardware capability. A knob on
/// <see cref="ExecutionPolicy"/>; capability detection itself stays
/// separate and process-wide, so this names a deliberate ceiling,
/// not a re-detection.
/// </summary>
/// <remarks>
/// <para>
/// The cap names the widest vector rung the ladder may walk to: each
/// value admits itself and every narrower rung. It exists for two
/// concrete cases — a SKU that down-clocks under wide-vector load (so
/// a narrower rung is faster end-to-end than the widest the hardware
/// advertises), and forced force-narrow or force-portable measurement
/// passes that exercise the slower rungs on hardware that supports the
/// wider ones, per the keep-measured-alternatives discipline.
/// </para>
/// </remarks>
public enum KernelWidthCap
{
    /// <summary>No cap below hardware capability: the ladder walks to the widest rung the machine supports. The default.</summary>
    Auto,

    /// <summary>Admit at most 256-bit vectors; narrower rungs (128-bit, portable) remain available below it.</summary>
    Bits256,

    /// <summary>Admit at most 128-bit vectors; the portable scalar rung remains available below it.</summary>
    Bits128,

    /// <summary>Force the portable scalar rung: no vector backend is selected regardless of hardware capability.</summary>
    Portable,
}
