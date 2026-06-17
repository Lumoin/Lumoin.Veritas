namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Verifies the integrity of a single addressable block the first time it is touched — the per-block
/// detection routine a <see cref="FirstTouchVerificationMap"/> runs before a block's bytes are decoded.
/// </summary>
/// <remarks>
/// The implementation recomputes the block's checksum over its bytes and compares it to the stored
/// digest (the same recompute-and-compare the decode-free verify round performs per blob), carrying
/// the image, the resolved <see cref="ChecksumAlgorithm"/>, and the block geometry as its own instance
/// state — never as a captured local — so the gate stays a pure function of the block index.
/// </remarks>
/// <param name="blockIndex">The zero-based block index to verify.</param>
/// <returns>
/// <see langword="true"/> when the block's bytes verify clean; <see langword="false"/> when corruption
/// is detected and the block must not be decoded.
/// </returns>
public delegate bool VerifyBlockDelegate(int blockIndex);
