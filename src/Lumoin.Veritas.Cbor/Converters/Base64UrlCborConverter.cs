using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a byte sequence to and from a CBOR Tag 33 data item
/// (RFC 8949 §3.4.5.2): a tagged text string carrying base64url-encoded
/// data per RFC 4648 §5.
/// </summary>
public sealed class Base64UrlCborConverter: CborConverter<byte[]>
{
    /// <inheritdoc/>
    public override void Write(CborWriter writer, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteTag(CborTag.Base64Url);
        string encoded = Base64Url.EncodeToString(value);
        writer.WriteTextString(encoded);
    }

    /// <inheritdoc/>
    public override byte[] Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.Base64Url)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 33 (Base64Url); got tag {tag.Value}."));
        }
        string text = reader.ReadTextString();
        byte[] textBytes = Encoding.ASCII.GetBytes(text);
        byte[] output = new byte[Base64Url.GetMaxDecodedLength(textBytes.Length)];
        OperationStatus status = Base64Url.DecodeFromUtf8(textBytes, output, out _, out int written);
        if(status != OperationStatus.Done)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR Tag 33 (Base64Url) content is not valid base64url: status {status}."));
        }
        return output[..written];
    }
}
