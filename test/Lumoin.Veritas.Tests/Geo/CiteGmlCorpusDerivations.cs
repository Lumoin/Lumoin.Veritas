using System.Text;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The recorded transformation applied to a vendored corpus original to produce a runtime
/// adaptation twin. The rules are part of the corpus provenance record
/// (<c>Geo/CiteGmlCorpus/PROVENANCE.md</c>): the twins exist so the corpus exercises the
/// reader's geometry value space and not merely its coordinate-reference roster gate, and every
/// derived byte sequence is pinned by SHA-256 in <see cref="CiteGmlCorpusExpectations"/> so the
/// transformation itself cannot drift.
/// </summary>
internal enum CorpusDerivationRule
{
    /// <summary>
    /// Every <c>srsName</c> value outside the reader's closed six-spelling roster is replaced with
    /// the canonical EPSG:4326 urn; a roster declaration is injected onto the root element only
    /// when the document carries no <c>srsName</c> attribute anywhere.
    /// </summary>
    CrsAdapted = 0,

    /// <summary>
    /// Every <c>srsName</c> value is replaced with the canonical EPSG:4326 urn, and a declaration
    /// is injected onto the root start tag when that tag itself carries none — exposing the
    /// behavior past the required-root-declaration gate for documents whose only declaration sat
    /// on a nested element.
    /// </summary>
    RootCrsAdapted = 1,

    /// <summary>
    /// The <c>aixm:Surface</c> root element is renamed <c>gml:Surface</c> and the aixm namespace
    /// declaration is removed (the element content is stock GML patches), then the document is
    /// adapted as <see cref="CrsAdapted"/> adapts it.
    /// </summary>
    Renamespaced = 2,
}

/// <summary>
/// Applies a <see cref="CorpusDerivationRule"/> to a vendored original. Line ends are normalized
/// to line feed first so every pinned refusal anchor is newline-convention independent; the
/// output bytes are UTF-8 without a byte-order mark.
/// </summary>
internal static class CiteGmlCorpusDerivations
{
    /// <summary>The canonical roster spelling injected and substituted by every rule.</summary>
    private const string CanonicalUrn = "urn:ogc:def:crs:EPSG::4326";

    /// <summary>The attribute-opening the substitution scan keys on.</summary>
    private const string AttributeOpening = "srsName=\"";

    /// <summary>The reader's closed roster; values already inside it are never substituted.</summary>
    private static string[] RosterSpellings { get; } =
    [
        "http://www.opengis.net/def/crs/OGC/1.3/CRS84",
        "http://www.opengis.net/def/crs/EPSG/0/4326",
        "http://www.opengis.net/def/crs/EPSG/0/3857",
        "urn:ogc:def:crs:OGC:1.3:CRS84",
        "urn:ogc:def:crs:EPSG::4326",
        "urn:ogc:def:crs:EPSG::3857",
    ];

    /// <summary>Derives the twin bytes for <paramref name="sourceText"/> under <paramref name="rule"/>.</summary>
    public static byte[] Derive(string sourceText, CorpusDerivationRule rule)
    {
        string text = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal);
        if(rule == CorpusDerivationRule.Renamespaced)
        {
            text = text
                .Replace("<aixm:Surface", "<gml:Surface", StringComparison.Ordinal)
                .Replace("</aixm:Surface>", "</gml:Surface>", StringComparison.Ordinal);
            text = RemoveAixmNamespaceDeclaration(text);
        }

        bool replaceRosterValuesToo = rule == CorpusDerivationRule.RootCrsAdapted;
        text = SubstituteCoordinateReferences(text, replaceRosterValuesToo);
        if(rule == CorpusDerivationRule.RootCrsAdapted)
        {
            text = InjectRootDeclarationWhenRootTagLacksOne(text);
        }
        else if(!text.Contains(AttributeOpening, StringComparison.Ordinal))
        {
            text = InjectRootDeclarationWhenRootTagLacksOne(text);
        }

        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Removes the single aixm namespace declaration together with the one whitespace character
    /// preceding it, mirroring the recorded vendoring-time transformation exactly.
    /// </summary>
    private static string RemoveAixmNamespaceDeclaration(string text)
    {
        int start = text.IndexOf("xmlns:aixm=\"", StringComparison.Ordinal);
        if(start < 0)
        {
            return text;
        }

        int valueStart = start + "xmlns:aixm=\"".Length;
        int valueEnd = text.IndexOf('"', valueStart);

        return text.Remove(start - 1, valueEnd - start + 2);
    }

    /// <summary>Replaces srsName attribute values, honoring the roster unless told to replace everything.</summary>
    private static string SubstituteCoordinateReferences(string text, bool replaceRosterValuesToo)
    {
        var result = new StringBuilder(text.Length);
        int position = 0;
        while(true)
        {
            int attribute = text.IndexOf(AttributeOpening, position, StringComparison.Ordinal);
            if(attribute < 0)
            {
                result.Append(text, position, text.Length - position);

                break;
            }

            int valueStart = attribute + AttributeOpening.Length;
            int valueEnd = text.IndexOf('"', valueStart);
            string value = text[valueStart..valueEnd];
            bool inRoster = false;
            foreach(string spelling in RosterSpellings)
            {
                if(string.Equals(value, spelling, StringComparison.Ordinal))
                {
                    inRoster = true;

                    break;
                }
            }

            result.Append(text, position, valueStart - position);
            result.Append(replaceRosterValuesToo || !inRoster ? CanonicalUrn : value);
            position = valueEnd;
        }

        return result.ToString();
    }

    /// <summary>
    /// Injects the canonical declaration directly after the root element's name when the root
    /// start tag carries no srsName attribute. The root is the first tag opening with a name
    /// character, which skips the declaration, comments, and any document type refuse the
    /// scanner would reject anyway.
    /// </summary>
    private static string InjectRootDeclarationWhenRootTagLacksOne(string text)
    {
        int rootStart = -1;
        for(int index = 0; index < text.Length - 1; index++)
        {
            if(text[index] == '<' && (char.IsLetter(text[index + 1]) || text[index + 1] == '_'))
            {
                rootStart = index;

                break;
            }
        }

        if(rootStart < 0)
        {
            return text;
        }

        int tagClose = text.IndexOf('>', rootStart);
        if(text.IndexOf(AttributeOpening, rootStart, StringComparison.Ordinal) is int existing
            && existing >= 0
            && existing < tagClose)
        {
            return text;
        }

        int nameEnd = rootStart + 1;
        while(nameEnd < text.Length && !char.IsWhiteSpace(text[nameEnd]) && text[nameEnd] != '>' && text[nameEnd] != '/')
        {
            nameEnd++;
        }

        return text.Insert(nameEnd, $" srsName=\"{CanonicalUrn}\"");
    }
}
