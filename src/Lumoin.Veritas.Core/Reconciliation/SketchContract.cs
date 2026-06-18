using System;
using System.IO;
using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// The geometry an integrity sketch's coded symbols are laid out under: the reconciliation item width, the
/// per-symbol checksum width (their sum is the serialized symbol width), and the symbols per storage block.
/// A loaded sketch is refused unless its geometry matches the reader's contract, so two replicas only ever
/// combine streams whose symbols split into the same byte fields.
/// </summary>
/// <remarks>
/// The item width is pinned to <see cref="ContentKey128.ByteWidth"/> — the frozen structural item key — so the
/// domain is implicitly structural; a wider content-hash item is a future contract behind a required-feature
/// flag, never a silent widening of this one. The 128-bit reconciliation checksum key is deliberately NOT
/// carried here: it lives host-side in the reconciliation library's own contract and is already folded into
/// every symbol's keyed checksum bytes, so this geometry contract neither holds nor needs it.
/// </remarks>
public readonly record struct SketchContract
{
    /// <summary>Creates a sketch geometry contract, validating that the item width is the frozen structural key width and the fields are in range.</summary>
    /// <param name="itemWidth">The reconciliation item width in bytes; must equal <see cref="ContentKey128.ByteWidth"/>.</param>
    /// <param name="checksumWidth">The per-symbol checksum width in bytes; one through eight.</param>
    /// <param name="symbolsPerBlock">The number of symbols per storage block; positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The item width is not the structural key width, the checksum width is outside one through eight, or the symbols per block is not positive.</exception>
    public SketchContract(int itemWidth, int checksumWidth, int symbolsPerBlock)
    {
        if(itemWidth != ContentKey128.ByteWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(itemWidth), itemWidth, $"The structural sketch item width must be {ContentKey128.ByteWidth} bytes (the frozen content-key width).");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(checksumWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(checksumWidth, 8);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(symbolsPerBlock);

        ItemWidth = itemWidth;
        ChecksumWidth = checksumWidth;
        SymbolsPerBlock = symbolsPerBlock;
    }

    /// <summary>The reconciliation item width in bytes — the frozen structural content-key width.</summary>
    public int ItemWidth { get; }

    /// <summary>The per-symbol checksum width in bytes.</summary>
    public int ChecksumWidth { get; }

    /// <summary>The number of coded symbols per storage block.</summary>
    public int SymbolsPerBlock { get; }

    /// <summary>The serialized width of one coded symbol: the item width plus the checksum width.</summary>
    public int SymbolWidth => ItemWidth + ChecksumWidth;

    /// <summary>The well-known structural contract: a 16-byte content-key item, an 8-byte checksum (a 24-byte symbol), 256 symbols per block (about 6 KiB per block).</summary>
    public static SketchContract Structural { get; } = new(ContentKey128.ByteWidth, 8, 256);

    /// <summary>Refuses a loaded sketch whose stored geometry does not match the expected contract — a right-checksum, wrong-geometry sketch would split symbols into an incompatible byte space and silently fail to peel, so it is a hard refusal, not a renegotiation.</summary>
    /// <param name="expected">The contract the reader requires.</param>
    /// <param name="loadedSymbolWidth">The symbol width read back from the loaded sketch image.</param>
    /// <param name="loadedSymbolsPerBlock">The symbols per block read back from the loaded sketch image.</param>
    /// <exception cref="InvalidDataException">The loaded geometry differs from <paramref name="expected"/>.</exception>
    public static void RequireMatch(SketchContract expected, int loadedSymbolWidth, int loadedSymbolsPerBlock)
    {
        if(loadedSymbolWidth != expected.SymbolWidth || loadedSymbolsPerBlock != expected.SymbolsPerBlock)
        {
            throw new InvalidDataException($"The loaded sketch geometry (symbol width {loadedSymbolWidth}, {loadedSymbolsPerBlock} symbols per block) does not match the expected contract (symbol width {expected.SymbolWidth}, {expected.SymbolsPerBlock} symbols per block).");
        }
    }
}
