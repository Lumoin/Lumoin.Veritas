using System.Text;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Shared GML documents and composition helpers for the reader and writer
/// families. A document lives here when more than one test compares against it or
/// when it is generated; one-off adversarial row literals stay inline in their
/// rows. The composers keep the namespace declaration and the root system
/// declaration in one place so rows state only what they test.
/// </summary>
internal static class GmlTestDocuments
{
    /// <summary>The GML namespace declaration every composed root carries.</summary>
    public const string NamespaceDeclaration = "xmlns:gml=\"http://www.opengis.net/gml/3.2\"";

    /// <summary>The canonical CRS84 system IRI.</summary>
    public const string Crs84 = "http://www.opengis.net/def/crs/OGC/1.3/CRS84";

    /// <summary>The canonical EPSG:4326 system IRI.</summary>
    public const string Epsg4326 = "http://www.opengis.net/def/crs/EPSG/0/4326";

    /// <summary>The canonical Web Mercator system IRI.</summary>
    public const string WebMercator = "http://www.opengis.net/def/crs/EPSG/0/3857";

    /// <summary>Composes a root geometry element under the CRS84 system.</summary>
    public static string Root(string localName, string content)
    {
        return Root(localName, content, Crs84, string.Empty);
    }

    /// <summary>Composes a root geometry element under a chosen system.</summary>
    public static string Root(string localName, string content, string system)
    {
        return Root(localName, content, system, string.Empty);
    }

    /// <summary>Composes a root geometry element under a chosen system with extra root attributes.</summary>
    public static string Root(string localName, string content, string system, string extraAttributes)
    {
        return $"<gml:{localName} {NamespaceDeclaration} srsName=\"{system}\"{extraAttributes}>{content}</gml:{localName}>";
    }

    /// <summary>Composes a root geometry element with NO system declaration — the absent-declaration refusal's fixture.</summary>
    public static string RootWithoutSystem(string localName, string content)
    {
        return $"<gml:{localName} {NamespaceDeclaration}>{content}</gml:{localName}>";
    }

    /// <summary>The canonical point body.</summary>
    public const string PointBody = "<gml:pos>1 2</gml:pos>";

    /// <summary>A four-position closed square ring as a position list.</summary>
    public const string SquareRing = "<gml:posList>0 0 4 0 4 4 0 0</gml:posList>";

    /// <summary>A smaller interior ring inside the square.</summary>
    public const string InnerRing = "<gml:posList>1 1 2 1 2 2 1 1</gml:posList>";

    /// <summary>The square polygon body: one exterior, no interiors.</summary>
    public const string SquarePolygonBody = "<gml:exterior><gml:LinearRing><gml:posList>0 0 4 0 4 4 0 0</gml:posList></gml:LinearRing></gml:exterior>";

    /// <summary>A curve whose sole segment is a linear one — the simplest curve body.</summary>
    public const string LinearCurveBody = "<gml:segments><gml:LineStringSegment><gml:posList>0 0 1 1 2 0</gml:posList></gml:LineStringSegment></gml:segments>";

    /// <summary>A curve joining a linear segment to a circular arc at the shared position.</summary>
    public const string JoinedCurveBody = "<gml:segments><gml:LineStringSegment><gml:posList>-2 0 0 -1</gml:posList></gml:LineStringSegment><gml:Arc><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc></gml:segments>";

    /// <summary>A ring bounded by one curve whose sole segment is a full three-point circle.</summary>
    public const string CircleRingPolygonBody = "<gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:Circle><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Circle></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>";

    /// <summary>A center-and-radius circle as a curve's sole segment, in degrees.</summary>
    public const string CenterRadiusCurveBody = "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>";

    /// <summary>A surface with one planar patch — the polygon normalization's fixture.</summary>
    public const string OnePatchSurfaceBody = "<gml:patches><gml:PolygonPatch><gml:exterior><gml:LinearRing><gml:posList>0 0 4 0 4 4 0 0</gml:posList></gml:LinearRing></gml:exterior></gml:PolygonPatch></gml:patches>";

    /// <summary>Builds a heterogeneous collection nest of the given wrapper count around a supplied leaf geometry element.</summary>
    public static string NestedCollectionsAround(int wrappers, string leafElement)
    {
        StringBuilder document = new();
        document.Append("<gml:MultiGeometry ").Append(NamespaceDeclaration).Append(" srsName=\"").Append(Crs84).Append("\">");

        for(int level = 1; level < wrappers; level++)
        {
            document.Append("<gml:geometryMember><gml:MultiGeometry>");
        }

        document.Append("<gml:geometryMember>").Append(leafElement).Append("</gml:geometryMember>");

        for(int level = 1; level < wrappers; level++)
        {
            document.Append("</gml:MultiGeometry></gml:geometryMember>");
        }

        document.Append("</gml:MultiGeometry>");

        return document.ToString();
    }

    /// <summary>Builds a heterogeneous collection nest of the given depth around a point leaf.</summary>
    public static string NestedCollections(int depth)
    {
        StringBuilder document = new();
        document.Append("<gml:MultiGeometry ").Append(NamespaceDeclaration).Append(" srsName=\"").Append(Crs84).Append("\">");

        for(int level = 1; level < depth; level++)
        {
            document.Append("<gml:geometryMember><gml:MultiGeometry>");
        }

        document.Append("<gml:geometryMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember>");

        for(int level = 1; level < depth; level++)
        {
            document.Append("</gml:MultiGeometry></gml:geometryMember>");
        }

        document.Append("</gml:MultiGeometry>");

        return document.ToString();
    }
}
