using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

/// <summary>
/// Reference codec implementations used by the test suite. The
/// production library ships no concrete typed-value codecs; the
/// consumer wires them at app start. This static class is the test
/// project's wiring.
/// </summary>
internal static class CborLdTestSetup
{
    /// <summary>URL encoder — packs an integer id (read from either CborLdInputInt or via type-table lookup) as a big-endian byte string.</summary>
    public static CborLdTypedValueEncodeDelegate UrlEncoder { get; } = (value, pool) =>
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pool);

        long id = value switch
        {
            CborLdInputInt i => i.Value,
            _ => throw new ArgumentException($"URL encoder expects CborLdInputInt; got {value.GetType().Name}.", nameof(value))
        };
        return RentBigEndian(pool, id);
    };

    /// <summary>URL decoder — reads a big-endian integer from the wire bytes into a CborLdInputInt.</summary>
    public static CborLdTypedValueDecodeDelegate UrlDecoder { get; } = bytes =>
    {
        long id = ReadBigEndian(bytes.Span);
        return new CborLdInputInt(id);
    };

    /// <summary>Date encoder — accepts an ISO 8601 date string, emits days-since-epoch as a 4-byte big-endian integer.</summary>
    public static CborLdTypedValueEncodeDelegate DateEncoder { get; } = (value, pool) =>
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pool);

        if(value is not CborLdInputString s)
        {
            throw new ArgumentException($"Date encoder expects CborLdInputString; got {value.GetType().Name}.", nameof(value));
        }
        if(!DateOnly.TryParseExact(s.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            throw new ArgumentException($"Date encoder requires an ISO 8601 date; got '{s.Value}'.", nameof(value));
        }
        int days = date.DayNumber - Epoch.DayNumber;
        IMemoryOwner<byte> owner = pool.Rent(4);
        BinaryPrimitives.WriteInt32BigEndian(owner.Memory.Span[..4], days);
        return new TrimmedMemoryOwner(owner, 4);
    };

    /// <summary>Date decoder — reads 4-byte big-endian days-since-epoch and emits an ISO 8601 string.</summary>
    public static CborLdTypedValueDecodeDelegate DateDecoder { get; } = bytes =>
    {
        if(bytes.Length != 4)
        {
            throw new ArgumentException($"Date decoder requires 4 wire bytes; got {bytes.Length}.", nameof(bytes));
        }
        int days = BinaryPrimitives.ReadInt32BigEndian(bytes.Span);
        DateOnly date = Epoch.AddDays(days);
        return new CborLdInputString(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    };

    /// <summary>DateTime encoder — accepts a UTC RFC 3339 timestamp, emits Unix epoch seconds as 8-byte big-endian.</summary>
    public static CborLdTypedValueEncodeDelegate DateTimeEncoder { get; } = (value, pool) =>
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pool);

        if(value is not CborLdInputString s)
        {
            throw new ArgumentException($"DateTime encoder expects CborLdInputString; got {value.GetType().Name}.", nameof(value));
        }
        if(!DateTimeOffset.TryParse(s.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset dt))
        {
            throw new ArgumentException($"DateTime encoder requires an RFC 3339 timestamp; got '{s.Value}'.", nameof(value));
        }
        long seconds = dt.ToUnixTimeSeconds();
        IMemoryOwner<byte> owner = pool.Rent(8);
        BinaryPrimitives.WriteInt64BigEndian(owner.Memory.Span[..8], seconds);
        return new TrimmedMemoryOwner(owner, 8);
    };

    /// <summary>DateTime decoder — reads 8-byte big-endian Unix epoch seconds and emits ISO 8601 UTC string.</summary>
    public static CborLdTypedValueDecodeDelegate DateTimeDecoder { get; } = bytes =>
    {
        if(bytes.Length != 8)
        {
            throw new ArgumentException($"DateTime decoder requires 8 wire bytes; got {bytes.Length}.", nameof(bytes));
        }
        long seconds = BinaryPrimitives.ReadInt64BigEndian(bytes.Span);
        DateTimeOffset dt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return new CborLdInputString(dt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    };

    /// <summary>Base64Url encoder — accepts a base64url-encoded string, emits the decoded bytes.</summary>
    public static CborLdTypedValueEncodeDelegate Base64UrlEncoder { get; } = (value, pool) =>
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pool);

        if(value is not CborLdInputString s)
        {
            throw new ArgumentException($"Base64Url encoder expects CborLdInputString; got {value.GetType().Name}.", nameof(value));
        }
        byte[] ascii = Encoding.ASCII.GetBytes(s.Value);
        int maxLen = Base64Url.GetMaxDecodedLength(ascii.Length);
        IMemoryOwner<byte> owner = pool.Rent(maxLen);
        OperationStatus status = Base64Url.DecodeFromUtf8(ascii, owner.Memory.Span[..maxLen], out _, out int written);
        if(status != OperationStatus.Done)
        {
            owner.Dispose();
            throw new ArgumentException($"Base64Url encoder input is not valid base64url: '{s.Value}'.", nameof(value));
        }
        return new TrimmedMemoryOwner(owner, written);
    };

    /// <summary>Base64Url decoder — encodes the wire bytes as a base64url string.</summary>
    public static CborLdTypedValueDecodeDelegate Base64UrlDecoder { get; } = bytes =>
    {
        string encoded = Base64Url.EncodeToString(bytes.Span);
        return new CborLdInputString(encoded);
    };

    /// <summary>Type identifier the reference encoder/decoder pair handles for URL values.</summary>
    public const string UrlType = "url";

    /// <summary>Type identifier the reference encoder/decoder pair handles for xsd:date values.</summary>
    public const string DateType = "http://www.w3.org/2001/XMLSchema#date";

    /// <summary>Type identifier the reference encoder/decoder pair handles for xsd:dateTime values.</summary>
    public const string DateTimeType = "http://www.w3.org/2001/XMLSchema#dateTime";

    /// <summary>Type identifier the reference encoder/decoder pair handles for base64url-encoded binary.</summary>
    public const string Base64UrlType = "base64url";

    private static DateOnly Epoch { get; } = new(1970, 1, 1);

    /// <summary>
    /// Module initializer — wires the reference codecs into the static
    /// registry when this test assembly is loaded.
    /// </summary>
    [ModuleInitializer]
    public static void InitializeCodecs()
    {
        if(CborLdTypedValueCodecs.IsInitialized)
        {
            return;
        }
        CborLdTypedValueCodecs.Initialize(
            (typeName, context) => typeName switch
            {
                UrlType => UrlEncoder,
                DateType => DateEncoder,
                DateTimeType => DateTimeEncoder,
                Base64UrlType => Base64UrlEncoder,
                _ => throw new ArgumentException(
                    $"No CBOR-LD encoder registered for type '{typeName}'.", nameof(typeName))
            },
            (typeName, context) => typeName switch
            {
                UrlType => UrlDecoder,
                DateType => DateDecoder,
                DateTimeType => DateTimeDecoder,
                Base64UrlType => Base64UrlDecoder,
                _ => throw new ArgumentException(
                    $"No CBOR-LD decoder registered for type '{typeName}'.", nameof(typeName))
            });
    }

    private static TrimmedMemoryOwner RentBigEndian(MemoryPool<byte> pool, long value)
    {
        int width = SmallestWidth(value);
        IMemoryOwner<byte> owner = pool.Rent(width);
        Span<byte> span = owner.Memory.Span[..width];
        switch(width)
        {
            case 1:
            {
                span[0] = (byte)value;
                break;
            }
            case 2:
            {
                BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value);
                break;
            }
            case 4:
            {
                BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value);
                break;
            }
            default:
            {
                BinaryPrimitives.WriteUInt64BigEndian(span, (ulong)value);
                break;
            }
        }
        return new TrimmedMemoryOwner(owner, width);
    }

    private static long ReadBigEndian(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            8 => (long)BinaryPrimitives.ReadUInt64BigEndian(bytes),
            _ => ReadGeneral(bytes)
        };
    }

    private static long ReadGeneral(ReadOnlySpan<byte> bytes)
    {
        long result = 0;
        foreach(byte b in bytes)
        {
            result = (result << 8) | b;
        }
        return result;
    }

    private static int SmallestWidth(long value)
    {
        //Minimum 2-byte width matches the handoff's wire example for URL
        //ids (200 -> 0x00 0xC8); the symmetric decoder handles any width.
        if(value < 0)
        {
            return 8;
        }
        if(value <= ushort.MaxValue)
        {
            return 2;
        }
        if(value <= uint.MaxValue)
        {
            return 4;
        }
        return 8;
    }

    private sealed class TrimmedMemoryOwner: IMemoryOwner<byte>
    {
        private readonly IMemoryOwner<byte> inner;
        private readonly int length;

        public TrimmedMemoryOwner(IMemoryOwner<byte> inner, int length)
        {
            this.inner = inner;
            this.length = length;
        }

        public Memory<byte> Memory => inner.Memory[..length];

        public void Dispose() => inner.Dispose();
    }
}
