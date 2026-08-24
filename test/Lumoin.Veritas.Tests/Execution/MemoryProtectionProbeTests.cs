using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// The SMBIOS interpretation behind the memory-protection probe: a
/// Physical Memory Array reporting ECC reads as protected, one reporting
/// none/parity/CRC reads as unprotected, an absent or inconclusive array
/// reads as unknown, and the structure walker finds the array even
/// behind a preceding string-bearing structure. Exercised on synthetic
/// firmware bytes — the operating-system calls that obtain the real
/// table are the untested boundary.
/// </summary>
[TestClass]
internal sealed class MemoryProtectionProbeTests
{
    /// <summary>The SMBIOS Memory Error Correction value for multi-bit ECC.</summary>
    private const byte MultiBitEcc = 0x06;

    /// <summary>The SMBIOS Memory Error Correction value for single-bit ECC.</summary>
    private const byte SingleBitEcc = 0x05;

    /// <summary>The SMBIOS Memory Error Correction value for no correction.</summary>
    private const byte NoCorrection = 0x03;

    /// <summary>The SMBIOS Memory Error Correction value for parity (detection, not correction).</summary>
    private const byte Parity = 0x04;

    /// <summary>The SMBIOS Memory Error Correction value for unknown.</summary>
    private const byte Unknown = 0x02;

    /// <summary>Wraps SMBIOS structure bytes in a <c>RawSMBIOSData</c> header with the correct table length.</summary>
    /// <param name="structures">The concatenated SMBIOS structures.</param>
    /// <returns>The raw firmware payload.</returns>
    private static byte[] RawSmbios(params byte[] structures)
    {
        byte[] payload = new byte[8 + structures.Length];
        payload[1] = 3;
        payload[2] = 2;
        payload[4] = (byte)structures.Length;
        payload[5] = (byte)(structures.Length >> 8);
        structures.CopyTo(payload, 8);

        return payload;
    }

    /// <summary>Builds one Physical Memory Array (type 16) structure with the given error-correction value and no strings.</summary>
    /// <param name="errorCorrection">The Memory Error Correction byte.</param>
    /// <returns>The structure bytes, double-null terminated.</returns>
    private static byte[] PhysicalMemoryArray(byte errorCorrection)
    {
        //15-byte formatted area: type, length, handle(2), location, use, ECC, max capacity(4), error handle(2), device count(2).
        return
        [
            16, 15, 0x01, 0x00, 0x03, 0x03, errorCorrection, 0xFF, 0xFF, 0xFF, 0x7F, 0xFE, 0xFF, 0x01, 0x00,
            0x00, 0x00,
        ];
    }

    /// <summary>Builds a minimal type-0 (BIOS information) structure carrying one string, to precede the memory array in the walker test.</summary>
    /// <returns>The structure bytes, double-null terminated after the single string.</returns>
    private static byte[] StringBearingStructure()
    {
        //4-byte formatted area (type, length, handle), then one string "x" terminated, then the set terminator.
        return [0, 4, 0x02, 0x00, (byte)'x', 0x00, 0x00];
    }

    [TestMethod]
    public void EccArraysReadAsProtected()
    {
        Assert.IsTrue(MemoryProtectionProbe.InterpretSmbios(RawSmbios(PhysicalMemoryArray(MultiBitEcc))));
        Assert.IsTrue(MemoryProtectionProbe.InterpretSmbios(RawSmbios(PhysicalMemoryArray(SingleBitEcc))));
    }

    [TestMethod]
    public void NonCorrectingArraysReadAsUnprotected()
    {
        Assert.IsFalse(MemoryProtectionProbe.InterpretSmbios(RawSmbios(PhysicalMemoryArray(NoCorrection))));
        Assert.IsFalse(MemoryProtectionProbe.InterpretSmbios(RawSmbios(PhysicalMemoryArray(Parity))));
    }

    [TestMethod]
    public void AnInconclusiveOrAbsentArrayReadsAsUnknown()
    {
        //An unknown error-correction value is inconclusive.
        Assert.IsNull(MemoryProtectionProbe.InterpretSmbios(RawSmbios(PhysicalMemoryArray(Unknown))));

        //No Physical Memory Array at all is inconclusive.
        Assert.IsNull(MemoryProtectionProbe.InterpretSmbios(RawSmbios(StringBearingStructure())));

        //A truncated payload is inconclusive, never a false reading.
        Assert.IsNull(MemoryProtectionProbe.InterpretSmbios(new byte[] { 1, 2, 3 }));
        Assert.IsNull(MemoryProtectionProbe.InterpretSmbios(ReadOnlySpan<byte>.Empty));
    }

    [TestMethod]
    public void TheWalkerFindsTheArrayBehindAPrecedingStringBearingStructure()
    {
        List<byte> structures = [];
        structures.AddRange(StringBearingStructure());
        structures.AddRange(PhysicalMemoryArray(MultiBitEcc));

        Assert.IsTrue(MemoryProtectionProbe.InterpretSmbios(RawSmbios([.. structures])));
    }
}
