using System.Text.Json;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The Internet JSON (RFC 7493) assertion the JSON-emitting test families
/// share: a conforming emission carries only ASCII bytes and never repeats
/// a member name within one object, at any nesting level. One documented
/// walk instead of per-family ad-hoc helpers.
/// </summary>
internal static class InternetJsonAssert
{
    /// <summary>
    /// Asserts the document satisfies the two Internet JSON properties a
    /// geometry writer can actually break: every byte below 0x80, and every
    /// member name unique within its object.
    /// </summary>
    public static void IsClean(ReadOnlySpan<byte> document)
    {
        foreach(byte value in document)
        {
            Assert.IsLessThanOrEqualTo((byte)0x7F, value, "every emitted byte is ASCII");
        }

        var reader = new Utf8JsonReader(document);
        var scopes = new Stack<HashSet<string>>();

        while(reader.Read())
        {
            if(reader.TokenType == JsonTokenType.StartObject)
            {
                scopes.Push([]);
            }
            else if(reader.TokenType == JsonTokenType.EndObject)
            {
                scopes.Pop();
            }
            else if(reader.TokenType == JsonTokenType.PropertyName)
            {
                Assert.IsTrue(scopes.Peek().Add(reader.GetString()!), "no member name repeats within one object");
            }
        }
    }
}
