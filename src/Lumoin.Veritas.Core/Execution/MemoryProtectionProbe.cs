using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Best-effort detection of whether main memory is hardware
/// error-corrected, feeding the <see cref="MemoryProtectionAssumption.AutoDetect"/>
/// resolution. The probe is deliberately conservative: it reports
/// <c>true</c> only on an affirmative hardware reading, <c>false</c> on
/// an affirmative "no correction" reading, and <c>null</c> when it
/// cannot tell — and an unknown reading resolves to unprotected (verify
/// more, not less), so a missed detection never weakens protection.
/// </summary>
/// <remarks>
/// <para>
/// The probe is unreliable exactly where large data deploys —
/// hypervisors and containers present synthetic firmware tables — which
/// is why the operator can always override it with an explicit
/// assumption. The parse of the firmware table is a pure function so its
/// verdicts are tested on synthetic input; the operating-system calls
/// that obtain the table are the thin, untested boundary.
/// </para>
/// </remarks>
internal static class MemoryProtectionProbe
{
    /// <summary>The SMBIOS structure type for a Physical Memory Array, whose Memory Error Correction field carries the verdict.</summary>
    private const byte PhysicalMemoryArrayType = 16;

    /// <summary>The SMBIOS end-of-table structure type.</summary>
    private const byte EndOfTableType = 127;

    /// <summary>The byte offset of the Memory Error Correction field within a Physical Memory Array structure.</summary>
    private const int MemoryErrorCorrectionOffset = 6;

    /// <summary>The 'RSMB' raw-SMBIOS firmware-table provider signature for <c>GetSystemFirmwareTable</c>.</summary>
    private const uint RawSmbiosProvider = 0x52534D42;

    /// <summary>The path whose presence indicates the Linux EDAC driver bound a memory controller — taken as evidence of error-correcting memory.</summary>
    private const string LinuxEdacMemoryControllerPath = "/sys/devices/system/edac/mc/mc0";

    /// <summary>
    /// Detects the memory-protection state of the running host.
    /// </summary>
    /// <returns><c>true</c> when memory is error-corrected, <c>false</c> when it is affirmatively not, and <c>null</c> when the host cannot be probed (a browser, a hypervisor with synthetic firmware, or any read failure).</returns>
    public static bool? Detect()
    {
        if(OperatingSystem.IsWindows())
        {
            return DetectWindows();
        }

        if(OperatingSystem.IsLinux())
        {
            return DetectLinux();
        }

        return null;
    }

    /// <summary>
    /// Parses a raw SMBIOS table — the payload <c>GetSystemFirmwareTable</c>
    /// returns for the 'RSMB' provider — and reads the Memory Error
    /// Correction field of the first conclusive Physical Memory Array.
    /// </summary>
    /// <param name="rawSmbios">The raw SMBIOS payload, beginning with its <c>RawSMBIOSData</c> header.</param>
    /// <returns><c>true</c> for an ECC array, <c>false</c> for none/parity/CRC (detection without correction), and <c>null</c> when no array is conclusive or the payload is malformed.</returns>
    internal static bool? InterpretSmbios(ReadOnlySpan<byte> rawSmbios)
    {
        //RawSMBIOSData header: [0..3] calling-method/version bytes, [4..7] table length, [8..] the structures.
        const int HeaderLength = 8;
        if(rawSmbios.Length < HeaderLength)
        {
            return null;
        }

        uint tableLength = BinaryPrimitives.ReadUInt32LittleEndian(rawSmbios.Slice(4, 4));
        ReadOnlySpan<byte> table = rawSmbios[HeaderLength..];
        if(tableLength < (uint)table.Length)
        {
            table = table[..(int)tableLength];
        }

        int offset = 0;
        while(offset + 4 <= table.Length)
        {
            byte structureType = table[offset];
            byte formattedLength = table[offset + 1];
            if(formattedLength < 4 || structureType == EndOfTableType)
            {
                break;
            }

            if(structureType == PhysicalMemoryArrayType && formattedLength > MemoryErrorCorrectionOffset && offset + MemoryErrorCorrectionOffset < table.Length)
            {
                bool? verdict = InterpretErrorCorrection(table[offset + MemoryErrorCorrectionOffset]);
                if(verdict is not null)
                {
                    return verdict;
                }
            }

            offset = NextStructureOffset(table, offset, formattedLength);
        }

        return null;
    }

    /// <summary>Maps the SMBIOS Memory Error Correction enumeration to a protection verdict.</summary>
    /// <param name="errorCorrection">The Memory Error Correction byte.</param>
    /// <returns><c>true</c> for single- or multi-bit ECC, <c>false</c> for none/parity/CRC, and <c>null</c> for other/unknown.</returns>
    private static bool? InterpretErrorCorrection(byte errorCorrection)
    {
        return errorCorrection switch
        {
            //0x05 single-bit ECC, 0x06 multi-bit ECC — error-correcting.
            0x05 or 0x06 => true,
            //0x03 none, 0x04 parity, 0x07 CRC — detection at most, not correction.
            0x03 or 0x04 or 0x07 => false,
            //0x01 other, 0x02 unknown — inconclusive.
            _ => null,
        };
    }

    /// <summary>Advances past one SMBIOS structure: its formatted area, then its double-null-terminated string set.</summary>
    /// <param name="table">The SMBIOS structure table.</param>
    /// <param name="offset">The current structure's offset.</param>
    /// <param name="formattedLength">The current structure's formatted-area length.</param>
    /// <returns>The next structure's offset, past the end of the table when none remains.</returns>
    private static int NextStructureOffset(ReadOnlySpan<byte> table, int offset, byte formattedLength)
    {
        int cursor = offset + formattedLength;
        while(cursor + 1 < table.Length && (table[cursor] != 0 || table[cursor + 1] != 0))
        {
            cursor++;
        }

        //Past the terminating double null; if the table is malformed, force termination by leaving the loop bound.
        return cursor <= offset ? table.Length : cursor + 2;
    }

    /// <summary>Probes Linux: the EDAC memory-controller node's presence is evidence of error-correcting memory; its absence is inconclusive.</summary>
    /// <returns><c>true</c> when the EDAC node exists, otherwise <c>null</c>.</returns>
    [SupportedOSPlatform("linux")]
    private static bool? DetectLinux()
    {
        return Directory.Exists(LinuxEdacMemoryControllerPath) ? true : null;
    }

    /// <summary>Probes Windows: reads the raw SMBIOS table and interprets the Physical Memory Array. Any interop failure resolves to inconclusive.</summary>
    /// <returns>The interpreted verdict, or <c>null</c> on failure.</returns>
    [SupportedOSPlatform("windows")]
    private static bool? DetectWindows()
    {
        try
        {
            uint size = GetSystemFirmwareTable(RawSmbiosProvider, 0, null, 0);
            if(size == 0)
            {
                return null;
            }

            byte[] buffer = new byte[size];
            uint written = GetSystemFirmwareTable(RawSmbiosProvider, 0, buffer, size);
            if(written == 0 || written > size)
            {
                return null;
            }

            return InterpretSmbios(buffer.AsSpan(0, (int)written));
        }
        catch(Exception)
        {
            return null;
        }
    }

    /// <summary>Retrieves a system firmware table; the 'RSMB' provider returns the raw SMBIOS data.</summary>
    /// <param name="firmwareTableProviderSignature">The provider signature.</param>
    /// <param name="firmwareTableId">The table identifier (zero for 'RSMB').</param>
    /// <param name="firmwareTableBuffer">The destination buffer, or <c>null</c> to query the required size.</param>
    /// <param name="bufferSize">The destination buffer size.</param>
    /// <returns>The required size when querying, or the bytes written, or zero on failure.</returns>
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows")]
    private static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, [Out] byte[]? firmwareTableBuffer, uint bufferSize);
}
