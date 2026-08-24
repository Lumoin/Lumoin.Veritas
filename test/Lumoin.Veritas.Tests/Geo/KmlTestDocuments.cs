using System.Text;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Shared KML documents and composition helpers for the reader and writer
/// families. A document lives here when more than one test compares against it or
/// when it is generated; one-off adversarial row literals stay inline in their
/// rows. The composers keep the namespace declaration in one place so rows state
/// only what they test — the format fixes its coordinate system, so no system
/// parameter exists anywhere.
/// </summary>
internal static class KmlTestDocuments
{
    /// <summary>The KML namespace declaration every composed root carries, in the default-namespace form.</summary>
    public const string NamespaceDeclaration = "xmlns=\"http://www.opengis.net/kml/2.2\"";

    /// <summary>The vendor extension namespace declaration the prohibition rows attach where they need it.</summary>
    public const string GxNamespaceDeclaration = "xmlns:gx=\"http://www.google.com/kml/ext/2.2\"";

    /// <summary>Composes a root geometry element in the default-namespace form.</summary>
    public static string Root(string localName, string content)
    {
        return Root(localName, content, string.Empty);
    }

    /// <summary>Composes a root geometry element with extra root attributes.</summary>
    public static string Root(string localName, string content, string extraAttributes)
    {
        return $"<{localName} {NamespaceDeclaration}{extraAttributes}>{content}</{localName}>";
    }

    /// <summary>Composes a root geometry element in the prefixed form — the fixture whose unqualified children land in no namespace.</summary>
    public static string PrefixedRoot(string localName, string content)
    {
        return $"<kml:{localName} xmlns:kml=\"http://www.opengis.net/kml/2.2\">{content}</kml:{localName}>";
    }

    /// <summary>The canonical point body.</summary>
    public const string PointCoordinates = "<coordinates>1,2</coordinates>";

    /// <summary>A four-tuple closed square ring run.</summary>
    public const string SquareRingCoordinates = "<coordinates>0,0 4,0 4,4 0,0</coordinates>";

    /// <summary>A smaller closed ring inside the square.</summary>
    public const string InnerRingCoordinates = "<coordinates>1,1 2,1 2,2 1,1</coordinates>";

    /// <summary>The square polygon body: one exterior boundary, no interiors.</summary>
    public const string SquarePolygonBody = "<outerBoundaryIs><LinearRing>" + SquareRingCoordinates + "</LinearRing></outerBoundaryIs>";

    /// <summary>A foreign-namespace subtree the tolerance rows drop into recognized content.</summary>
    public const string ForeignChild = "<x:extra xmlns:x=\"urn:example:profile\">notes<x:inner/></x:extra>";

    /// <summary>Builds an aggregate nest of the given wrapper count around a point leaf — members nest directly, the format's shape.</summary>
    public static string NestedCollections(int wrappers)
    {
        return NestedCollectionsAround(wrappers, "<Point>" + PointCoordinates + "</Point>");
    }

    /// <summary>Builds an aggregate nest of the given wrapper count around a supplied leaf geometry element.</summary>
    public static string NestedCollectionsAround(int wrappers, string leafElement)
    {
        StringBuilder document = new();
        document.Append("<MultiGeometry ").Append(NamespaceDeclaration).Append('>');

        for(int level = 1; level < wrappers; level++)
        {
            document.Append("<MultiGeometry>");
        }

        document.Append(leafElement);

        for(int level = 1; level < wrappers; level++)
        {
            document.Append("</MultiGeometry>");
        }

        document.Append("</MultiGeometry>");

        return document.ToString();
    }

    /// <summary>Builds one aggregate holding the given number of sibling aggregates, each carrying a point — the breadth fixture beside the depth ones.</summary>
    public static string SiblingCollections(int siblings)
    {
        StringBuilder document = new();
        document.Append("<MultiGeometry ").Append(NamespaceDeclaration).Append('>');

        for(int index = 0; index < siblings; index++)
        {
            document.Append("<MultiGeometry><Point>").Append(PointCoordinates).Append("</Point></MultiGeometry>");
        }

        document.Append("</MultiGeometry>");

        return document.ToString();
    }
}
