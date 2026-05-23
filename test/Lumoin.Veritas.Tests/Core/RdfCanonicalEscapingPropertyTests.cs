using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsCheck;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Property-based coverage of the canonical N-Quads literal escaping.
/// For any literal value, the canonical serialization must escape every
/// character that requires it and pass every other character through
/// verbatim, and a faithful unescaper must recover the original value.
/// </summary>
[TestClass]
internal sealed class RdfCanonicalEscapingPropertyTests
{
    private const long Iterations = 20_000;
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const char Space = ' ';
    private const char Delete = '\u007F';
    private const string LinePrefix = "<http://example.org/s> <http://example.org/p> \"";
    private const string LineSuffix = "\" .\n";

    private static HashDelegate Sha256 { get; } = SHA256.HashData;

    //Characters in U+0000..U+00FF exercise the C0 controls, printable ASCII, and the
    //Latin-1 / C1 range without producing the lone surrogates that would form ill-formed
    //strings. This is the interesting span for escaping rules.
    private static Gen<string> LiteralValues { get; } =
        Gen.Char[(char)0, (char)0x00FF].Array[0, 24].Select(static chars => new string(chars));

    [TestMethod]
    public void CanonicalEscapingRoundTripsThroughFaithfulUnescaper()
    {
        LiteralValues.Sample(static value =>
        {
            string body = CanonicalLiteralBody(value);
            string recovered = Unescape(body);

            Assert.AreEqual(value, recovered);
        }, iter: Iterations);
    }

    [TestMethod]
    public void CanonicalEscapingNeverEmitsRawControlCharacters()
    {
        LiteralValues.Sample(static value =>
        {
            string body = CanonicalLiteralBody(value);

            foreach(char c in body)
            {
                Assert.IsFalse(
                    c < Space || c == Delete,
                    $"Raw control character U+{(int)c:X4} present in canonical output.");
            }
        }, iter: Iterations);
    }

    private static string CanonicalLiteralBody(string value)
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(pool.Intern(value), new NamedNode(pool.Intern(XsdString))));

        string line = RdfCanonicalizer.Canonicalize([quad], Sha256);

        //The xsd:string literal renders as <s> <p> "BODY" .\n; slice the escaped body out.
        int start = LinePrefix.Length;
        int length = line.Length - LinePrefix.Length - LineSuffix.Length;
        return line.Substring(start, length);
    }

    private static string Unescape(string escaped)
    {
        StringBuilder sb = new(escaped.Length);
        int i = 0;
        while(i < escaped.Length)
        {
            char c = escaped[i];
            if(c != '\\')
            {
                sb.Append(c);
                i++;
                continue;
            }

            char marker = escaped[i + 1];
            switch(marker)
            {
                case 'b':
                {
                    sb.Append('\b');
                    i += 2;
                    break;
                }

                case 't':
                {
                    sb.Append('\t');
                    i += 2;
                    break;
                }

                case 'n':
                {
                    sb.Append('\n');
                    i += 2;
                    break;
                }

                case 'f':
                {
                    sb.Append('\f');
                    i += 2;
                    break;
                }

                case 'r':
                {
                    sb.Append('\r');
                    i += 2;
                    break;
                }

                case '"':
                {
                    sb.Append('"');
                    i += 2;
                    break;
                }

                case '\\':
                {
                    sb.Append('\\');
                    i += 2;
                    break;
                }

                case 'u':
                {
                    int code = int.Parse(escaped.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    sb.Append((char)code);
                    i += 6;
                    break;
                }

                default:
                {
                    throw new FormatException($"Unexpected escape marker '{marker}' in canonical output.");
                }
            }
        }

        return sb.ToString();
    }
}
