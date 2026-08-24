using System.Text;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The shared XML documents of the scanner family. A document lives here
/// when more than one test compares against it or when it is generated;
/// one-off adversarial row literals stay inline in their rows.
/// </summary>
internal static class XmlTestDocuments
{
    /// <summary>The smallest accepted document: one empty-element root.</summary>
    public const string MinimalRoot = "<a/>";

    /// <summary>The canonical full XML declaration in the accepted encoding.</summary>
    public const string CanonicalDeclaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    /// <summary>The reserved XML namespace name, spelled as documents declare it.</summary>
    public const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    /// <summary>The reserved declaration namespace name, spelled as documents would try to bind it.</summary>
    public const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    /// <summary>The number of bytes one nesting level's start tag contributes to <see cref="NestedElementChain"/>.</summary>
    public const int NestedElementOpenLength = 3;

    /// <summary>
    /// A chain of identically named elements nested to the requested depth,
    /// every level closed — the depth-cap fixture, generated so the
    /// boundary rows compute their offsets instead of hand-counting them.
    /// </summary>
    public static string NestedElementChain(int depth)
    {
        StringBuilder builder = new(depth * 7);
        for(int level = 0; level < depth; level++)
        {
            builder.Append("<e>");
        }

        for(int level = 0; level < depth; level++)
        {
            builder.Append("</e>");
        }

        return builder.ToString();
    }
}
