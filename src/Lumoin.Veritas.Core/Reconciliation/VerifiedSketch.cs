using System;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// A sketch's coded-symbol bytes that have passed load-time verification — every block's checksum was
/// confirmed before the bytes were handed out. Only <see cref="SketchPersistence.LoadVerifiedSketch"/>
/// constructs one and the decode seam (<see cref="SketchReconciliationDelegates.DecodeSketchDifference"/>)
/// consumes one, so a value of this type is the type-system evidence that detection preceded any combine: the
/// decode cannot be handed unverified bytes because it can only be handed a verified sketch.
/// </summary>
public readonly record struct VerifiedSketch
{
    /// <summary>Creates a verified sketch over bytes a verifying load has already checked.</summary>
    /// <param name="symbols">The verified coded-symbol bytes.</param>
    /// <param name="symbolWidth">The serialized width of one symbol in bytes.</param>
    /// <param name="symbolCount">The number of symbols.</param>
    internal VerifiedSketch(ReadOnlyMemory<byte> symbols, int symbolWidth, int symbolCount)
    {
        Symbols = symbols;
        SymbolWidth = symbolWidth;
        SymbolCount = symbolCount;
    }

    /// <summary>The verified coded-symbol bytes, in stored order.</summary>
    public ReadOnlyMemory<byte> Symbols { get; }

    /// <summary>The serialized width of one symbol in bytes.</summary>
    public int SymbolWidth { get; }

    /// <summary>The number of symbols.</summary>
    public int SymbolCount { get; }
}
