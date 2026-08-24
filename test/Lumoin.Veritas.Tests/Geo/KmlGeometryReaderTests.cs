using System;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The KML reader family: roster acceptance with reference-text cross-checks, the
/// tuple grammar with its one deliberate comma-whitespace tolerance, the linear
/// ring's one-way carriage as the closed line string, the position taxonomy over
/// namespaces and content models, the presentation-element skip contract, and the
/// rejection matrix over structural rules, tuple defects, and the security floor
/// through the public reader. Refusal rows assert the kind AND the byte offset,
/// and offsets are computed from markers, never hand-counted.
/// </summary>
[TestClass]
internal sealed class KmlGeometryReaderTests
{
    /// <summary>Roster documents equal their reference-text readings structurally and bitwise.</summary>
    [TestMethod]
    [DataRow("Point", "<coordinates>1,2</coordinates>", "POINT (1 2)", DisplayName = "the point materializes from its tuple")]
    [DataRow("Point", "<coordinates>1,2,5</coordinates>", "POINT Z (1 2 5)", DisplayName = "the third component carries the altitude")]
    [DataRow("LineString", "<coordinates>0,0 1,1 2,0</coordinates>", "LINESTRING (0 0, 1 1, 2 0)", DisplayName = "the coordinate run carries a line string")]
    [DataRow("LineString", "<coordinates>0,0,5 1,1,6</coordinates>", "LINESTRING Z (0 0 5, 1 1 6)", DisplayName = "a three-component run carries the altitudes")]
    [DataRow("LinearRing", "<coordinates>0,0 4,0 4,4 0,0</coordinates>", "LINESTRING (0 0, 4 0, 4 4, 0 0)", DisplayName = "the linear ring carries one way as the closed line string")]
    [DataRow("Polygon", KmlTestDocuments.SquarePolygonBody, "POLYGON ((0 0, 4 0, 4 4, 0 0))", DisplayName = "the polygon reads its exterior boundary")]
    [DataRow("Polygon", KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs><LinearRing>" + KmlTestDocuments.InnerRingCoordinates + "</LinearRing></innerBoundaryIs>", "POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", DisplayName = "the polygon reads its interior after the exterior")]
    [DataRow("Polygon", KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs><LinearRing>" + KmlTestDocuments.InnerRingCoordinates + "</LinearRing></innerBoundaryIs><innerBoundaryIs><LinearRing><coordinates>3,1 3.5,1 3.5,1.5 3,1</coordinates></LinearRing></innerBoundaryIs>", "POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1), (3 1, 3.5 1, 3.5 1.5, 3 1))", DisplayName = "the polygon reads two interiors — the zero-or-more permission above one")]
    [DataRow("MultiGeometry", "<Point><coordinates>1,2</coordinates></Point><LineString><coordinates>0,0 1,1</coordinates></LineString>", "GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", DisplayName = "the aggregate carries its members as the heterogeneous collection")]
    [DataRow("MultiGeometry", "<Point><coordinates>1,2</coordinates></Point>", "GEOMETRYCOLLECTION (POINT (1 2))", DisplayName = "a single-member aggregate is accepted — the more-than-one should is not adjudicated")]
    [DataRow("MultiGeometry", "<MultiGeometry><Point><coordinates>1,2</coordinates></Point></MultiGeometry>", "GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (1 2)))", DisplayName = "a nested aggregate recurses as a nested collection")]
    [DataRow("MultiGeometry", "<LinearRing><coordinates>0,0 4,0 4,4 0,0</coordinates></LinearRing>", "GEOMETRYCOLLECTION (LINESTRING (0 0, 4 0, 4 4, 0 0))", DisplayName = "the linear ring collapses at member position exactly as at the root")]
    [DataRow("MultiGeometry", "<Point><coordinates>1,2</coordinates></Point><Point><coordinates>3,4,5</coordinates></Point>", "GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5))", DisplayName = "members differ in dimension per member")]
    public void AcceptedDocumentsMatchTheirReferenceText(string localName, string body, string wkt)
    {
        KmlAssert.MatchesWkt(KmlTestDocuments.Root(localName, body), wkt);
    }

    /// <summary>The tuple grammar accepts the schema-strict whitespace forms and the one comma-adjacent tolerance.</summary>
    [TestMethod]
    [DataRow("<coordinates>\n  1,2\n  3,4\n</coordinates>", "LINESTRING (1 2, 3 4)", DisplayName = "newline separators and leading and trailing whitespace are the list type's own forms")]
    [DataRow("<coordinates>1,2\t3,4</coordinates>", "LINESTRING (1 2, 3 4)", DisplayName = "a tab separates tuples")]
    [DataRow("<coordinates>1,2   3,4</coordinates>", "LINESTRING (1 2, 3 4)", DisplayName = "a whitespace run separates tuples once")]
    [DataRow("<coordinates>1, 2 3 ,4</coordinates>", "LINESTRING (1 2, 3 4)", DisplayName = "whitespace adjacent to a comma binds to the comma — the one tolerance")]
    [DataRow("<coordinates>1,2&#13;3,4</coordinates>", "LINESTRING (1 2, 3 4)", DisplayName = "a carriage return separates tuples — the roster's fourth member, reachable only through a character reference")]
    public void TheTupleGrammarAcceptsItsForms(string body, string wkt)
    {
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("LineString", body), wkt);
    }

    /// <summary>The comma-whitespace tolerance binds maximal-munch: a spaced third component joins its tuple.</summary>
    [TestMethod]
    public void CommaAdjacentWhitespaceBindsMaximalMunch()
    {
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("Point", "<coordinates>1,2 ,3</coordinates>"), "POINT Z (1 2 3)");
    }

    /// <summary>The double grammar's signed, bare-fraction, and exponent spellings parse — the exponent acceptance is the recorded deviation row.</summary>
    [TestMethod]
    public void SignedFractionAndExponentSpellingsParse()
    {
        using FlatGeometry signed = KmlAssert.Accepts(KmlTestDocuments.Root("Point", "<coordinates>+1,.5</coordinates>"));
        Assert.AreEqual(new Point2d(1.0, 0.5), signed.Vertices[0], "the signed and bare-fraction spellings parse");

        using FlatGeometry exponent = KmlAssert.Accepts(KmlTestDocuments.Root("Point", "<coordinates>1e2,2.5e-1</coordinates>"));
        Assert.AreEqual(new Point2d(100.0, 0.25), exponent.Vertices[0], "the exponent spellings parse");
    }

    /// <summary>Coordinate domains are never validated: an out-of-range latitude parses and carries verbatim.</summary>
    [TestMethod]
    public void OutOfRangeOrdinatesCarryVerbatim()
    {
        using FlatGeometry geometry = KmlAssert.Accepts(KmlTestDocuments.Root("Point", "<coordinates>-118,92</coordinates>"));
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(92.0), BitConverter.DoubleToInt64Bits(geometry.Vertices[0].Y), "the out-of-range latitude rides bit for bit");
    }

    /// <summary>A ring closing on the planar predicate with differing altitudes is accepted — the closure tolerance against the tuple-identity rule.</summary>
    [TestMethod]
    public void RingClosureConsultsOnlyThePlanarOrdinates()
    {
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("LinearRing", "<coordinates>0,0,1 4,0,2 4,4,3 0,0,9</coordinates>"), "LINESTRING Z (0 0 1, 4 0 2, 4 4 3, 0 0 9)");
    }

    /// <summary>The presentation prefix is consumed without effect, and its values are never inspected.</summary>
    [TestMethod]
    [DataRow("<extrude>1</extrude><tessellate>true</tessellate><altitudeMode>absolute</altitudeMode>", DisplayName = "the full presentation prefix skips")]
    [DataRow("<extrude>banana</extrude>", DisplayName = "a non-boolean extrusion flag passes — skipped wholesale, the recorded tolerance")]
    [DataRow("<tessellate>1</tessellate>", DisplayName = "the draping flag skips alone")]
    [DataRow("<altitudeMode>clampToGround</altitudeMode>", DisplayName = "the clamped mode is consumed without effect")]
    [DataRow("<altitudeMode>relativeToGround</altitudeMode>", DisplayName = "the relative mode is consumed without effect")]
    [DataRow("<altitudeMode></altitudeMode>", DisplayName = "the exactly-empty paired mode is the schema default")]
    [DataRow("<altitudeMode/>", DisplayName = "the exactly-empty self-closed mode is the schema default")]
    public void PresentationElementsSkipWithoutEffect(string prefix)
    {
        using FlatGeometry withPrefix = KmlAssert.Accepts(KmlTestDocuments.Root("LineString", prefix + "<coordinates>0,0 1,1</coordinates>"));

        using FlatGeometry bare = KmlAssert.Accepts(KmlTestDocuments.Root("LineString", "<coordinates>0,0 1,1</coordinates>"));
        Assert.AreEqual(bare, withPrefix, "the presentation prefix changes nothing");
    }

    /// <summary>A boundary ring's presentation elements skip too — the recommendation against them is not adjudicated.</summary>
    [TestMethod]
    public void BoundaryRingPresentationElementsSkip()
    {
        string body = "<outerBoundaryIs><LinearRing><tessellate>1</tessellate><altitudeMode>clampToGround</altitudeMode>" + KmlTestDocuments.SquareRingCoordinates + "</LinearRing></outerBoundaryIs>";
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("Polygon", body), "POLYGON ((0 0, 4 0, 4 4, 0 0))");
    }

    /// <summary>A boundary ring's extrusion flag skips too — all three discouraged elements are admitted at boundary position.</summary>
    [TestMethod]
    public void BoundaryRingExtrusionFlagSkips()
    {
        string body = "<outerBoundaryIs><LinearRing><extrude>1</extrude>" + KmlTestDocuments.SquareRingCoordinates + "</LinearRing></outerBoundaryIs>";
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("Polygon", body), "POLYGON ((0 0, 4 0, 4 4, 0 0))");
    }

    /// <summary>Foreign-namespace children skip wholesale at any content position, their descendants uninspected — a vendor-extension descendant included.</summary>
    [TestMethod]
    [DataRow(KmlTestDocuments.ForeignChild + "<coordinates>0,0 1,1</coordinates>", DisplayName = "a foreign child before the run skips")]
    [DataRow("<coordinates>0,0 1,1</coordinates>" + KmlTestDocuments.ForeignChild, DisplayName = "a foreign child after the run skips")]
    [DataRow("<x:extra xmlns:x=\"urn:example:profile\" " + KmlTestDocuments.GxNamespaceDeclaration + "><gx:drape/></x:extra><coordinates>0,0 1,1</coordinates>", DisplayName = "a vendor-extension descendant inside a skipped subtree is uninspected — wholesale means wholesale")]
    [DataRow("<extrude><gx:flag " + KmlTestDocuments.GxNamespaceDeclaration + "/></extrude><coordinates>0,0 1,1</coordinates>", DisplayName = "the extrusion flag's interior is uninspected too")]
    public void ForeignChildrenSkipWholesale(string body)
    {
        using FlatGeometry tolerated = KmlAssert.Accepts(KmlTestDocuments.Root("LineString", body));

        using FlatGeometry bare = KmlAssert.Accepts(KmlTestDocuments.Root("LineString", "<coordinates>0,0 1,1</coordinates>"));
        Assert.AreEqual(bare, tolerated, "the skipped content changes nothing");
    }

    /// <summary>Attributes are ignored wholesale: identifiers, the undeclared version, foreign and vendor-extension and remote-reference attributes alike.</summary>
    [TestMethod]
    [DataRow(" id=\"42\" targetId=\"t1\"", DisplayName = "a numeric identifier and a target ride ignored — no identity is carried")]
    [DataRow(" version=\"2.3\"", DisplayName = "the undeclared version attribute is not consulted")]
    [DataRow(" x:meta=\"1\" xmlns:x=\"urn:example:profile\"", DisplayName = "a foreign-namespace attribute is ignored")]
    [DataRow(" gx:alt=\"1\" " + KmlTestDocuments.GxNamespaceDeclaration, DisplayName = "a vendor-extension attribute is ignored where the element refuses")]
    [DataRow(" xlink:href=\"#g\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"", DisplayName = "a remote-reference attribute is ignored — the format defines no reference semantics on geometry")]
    public void AttributesAreIgnoredWholesale(string extraAttributes)
    {
        using FlatGeometry attributed = KmlAssert.Accepts(KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates, extraAttributes));

        using FlatGeometry bare = KmlAssert.Accepts(KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates));
        Assert.AreEqual(bare, attributed, "the attributes change nothing");
    }

    /// <summary>The attribute tolerance is element-free: every roster element ignores every attribute class the format can carry.</summary>
    [TestMethod]
    [DataRow("LineString", "<coordinates>0,0 1,1</coordinates>", DisplayName = "the line string ignores its attributes")]
    [DataRow("LinearRing", KmlTestDocuments.SquareRingCoordinates, DisplayName = "the linear ring ignores its attributes")]
    [DataRow("Polygon", KmlTestDocuments.SquarePolygonBody, DisplayName = "the polygon ignores its attributes")]
    [DataRow("MultiGeometry", "<Point>" + KmlTestDocuments.PointCoordinates + "</Point>", DisplayName = "the aggregate ignores its attributes")]
    public void AttributeToleranceIsElementFree(string localName, string body)
    {
        const string everyClass = " id=\"42\" targetId=\"t1\" version=\"2.3\" x:meta=\"1\" xmlns:x=\"urn:example:profile\" gx:alt=\"1\" " + KmlTestDocuments.GxNamespaceDeclaration + " xlink:href=\"#g\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";
        using FlatGeometry attributed = KmlAssert.Accepts(KmlTestDocuments.Root(localName, body, everyClass));

        using FlatGeometry bare = KmlAssert.Accepts(KmlTestDocuments.Root(localName, body));
        Assert.AreEqual(bare, attributed, "the attributes change nothing on any roster element");
    }

    /// <summary>The remote-reference attribute is ignored at nested positions too — the format defines no reference semantics anywhere on geometry, not merely at the root.</summary>
    [TestMethod]
    [DataRow("MultiGeometry", "<Point xlink:href=\"#g\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">" + KmlTestDocuments.PointCoordinates + "</Point>", "<Point>" + KmlTestDocuments.PointCoordinates + "</Point>", DisplayName = "a remote reference on an aggregate member is ignored")]
    [DataRow("Polygon", "<outerBoundaryIs><LinearRing xlink:href=\"#g\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">" + KmlTestDocuments.SquareRingCoordinates + "</LinearRing></outerBoundaryIs>", KmlTestDocuments.SquarePolygonBody, DisplayName = "a remote reference on a boundary's ring is ignored")]
    public void TheRemoteReferenceAttributeIsIgnoredAtEveryPosition(string localName, string referencedBody, string bareBody)
    {
        using FlatGeometry referenced = KmlAssert.Accepts(KmlTestDocuments.Root(localName, referencedBody));

        using FlatGeometry bare = KmlAssert.Accepts(KmlTestDocuments.Root(localName, bareBody));
        Assert.AreEqual(bare, referenced, "the nested remote reference changes nothing");
    }

    /// <summary>The identifier constraints are unenforced: a duplicated identifier and a target outside the name lexical space both ride ignored.</summary>
    [TestMethod]
    public void IdentifierConstraintsAreUnenforced()
    {
        string body = "<Point id=\"dup\" targetId=\"9bad\"><coordinates>1,2</coordinates></Point><Point id=\"dup\"><coordinates>3,4</coordinates></Point>";
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("MultiGeometry", body, " id=\"dup\""), "GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4))");
    }

    /// <summary>The prefixed spelling reads identically to the default-namespace spelling — identity is the namespace, never the prefix.</summary>
    [TestMethod]
    public void ThePrefixedSpellingReadsIdentically()
    {
        using FlatGeometry prefixed = KmlAssert.Accepts(KmlTestDocuments.PrefixedRoot("Point", "<kml:coordinates>1,2</kml:coordinates>"));

        using FlatGeometry plain = KmlAssert.Accepts(KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates));
        Assert.AreEqual(plain, prefixed, "the two spellings carry one value");
    }

    /// <summary>A byte-order mark is the transport substrate's to consume; the document behind it reads whole.</summary>
    [TestMethod]
    public void AByteOrderMarkIsConsumed()
    {
        byte[] text = Encoding.UTF8.GetBytes(KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates));
        byte[] document = new byte[text.Length + 3];
        document[0] = 0xEF;
        document[1] = 0xBB;
        document[2] = 0xBF;
        text.CopyTo(document, 3);

        bool accepted = KmlGeometryReader.TryRead(document, out FlatGeometry geometry, out GeometryCodecRefusal refusal);
        Assert.IsTrue(accepted, $"the marked document must be accepted, but refused {refusal.Kind} at {refusal.ByteOffset}");

        using(geometry)
        {
            Assert.AreEqual(GeometryKind.Point, geometry.Kind, "the value reads behind the mark");
        }
    }

    /// <summary>Thirty-one aggregate wrappers accept and thirty-two refuse — the geometry bound's boundary pair.</summary>
    [TestMethod]
    public void TheGeometryBoundRefusesTheThirtySecondWrapper()
    {
        using FlatGeometry deep = KmlAssert.Accepts(KmlTestDocuments.NestedCollections(31));
        Assert.AreEqual(GeometryKind.GeometryCollection, deep.Kind, "thirty-one wrappers sit exactly at the geometry bound");

        KmlAssert.RefusesAt(KmlTestDocuments.NestedCollections(32), GeometryCodecRefusalKind.NestingTooDeep, "<Point>");
    }

    /// <summary>The geometry bound counts nesting, never aggregates opened: a wide document of sibling aggregates rides far under it.</summary>
    [TestMethod]
    public void TheGeometryBoundCountsNestingNotAggregatesOpened()
    {
        using FlatGeometry wide = KmlAssert.Accepts(KmlTestDocuments.SiblingCollections(33));
        Assert.AreEqual(GeometryKind.GeometryCollection, wide.Kind, "thirty-three sibling aggregates nest only two deep");
        Assert.AreEqual(33, wide.Nodes[0].ChildCount, "every sibling is carried");
    }

    /// <summary>Thirty-one wrappers around the deepest leaf chain accept — the transport cap carries every geometry-bound-legal document.</summary>
    [TestMethod]
    public void TheDepthBoundsCarryTheDeepestLeafInLockstep()
    {
        string leaf = "<Polygon>" + KmlTestDocuments.SquarePolygonBody + "</Polygon>";
        using FlatGeometry deep = KmlAssert.Accepts(KmlTestDocuments.NestedCollectionsAround(31, leaf));
        Assert.AreEqual(GeometryKind.GeometryCollection, deep.Kind, "the wrapped polygon chain rides under the transport cap");
    }

    /// <summary>The character-span convenience reports offsets into the transcoded UTF-8 representation, not character positions.</summary>
    [TestMethod]
    public void TheCharacterOverloadReportsTranscodedOffsets()
    {
        string document = "<!--ää-->" + "<Point><coordinates>1,2</coordinates></Point>";
        bool accepted = KmlGeometryReader.TryRead(document.AsSpan(), out FlatGeometry geometry, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "the namespace-less root must refuse");
        Assert.AreEqual(default, geometry, "the geometry out is default on every refusal");
        Assert.AreEqual(GeometryCodecRefusalKind.UnsupportedGeometry, refusal.Kind, "the root refusal kind");
        Assert.AreEqual(KmlAssert.ByteOffsetOf(document, "<Point"), refusal.ByteOffset, "the offset indexes the transcoded bytes, two wider than the character index here");
        Assert.AreNotEqual(document.IndexOf("<Point", StringComparison.Ordinal), refusal.ByteOffset, "the character index diverges on this non-ASCII input");
    }

    /// <summary>Geometry-identity positions refuse everything outside the roster: feature envelopes, the textured model, foreign and absent namespaces — and the vendor extension by prohibition before identity.</summary>
    [TestMethod]
    [DataRow("<kml " + KmlTestDocuments.NamespaceDeclaration + "><Placemark/></kml>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<kml ", DisplayName = "the document envelope refuses at the root name")]
    [DataRow("<Placemark " + KmlTestDocuments.NamespaceDeclaration + "><Point><coordinates>1,2</coordinates></Point></Placemark>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Placemark", DisplayName = "a feature root refuses at its name")]
    [DataRow("<Model " + KmlTestDocuments.NamespaceDeclaration + "></Model>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Model", DisplayName = "the textured model refuses at the root")]
    [DataRow("<point " + KmlTestDocuments.NamespaceDeclaration + "><coordinates>1,2</coordinates></point>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<point", DisplayName = "element names are case-sensitive — the lowercase spelling is not the roster's")]
    [DataRow("<Point><coordinates>1,2</coordinates></Point>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Point", DisplayName = "a namespace-less fragment refuses at the root")]
    [DataRow("<x:Point xmlns:x=\"urn:example:profile\"/>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<x:Point", DisplayName = "a foreign-namespace root refuses at its name")]
    [DataRow("<Point xmlns=\"http://www.opengis.net/kml/2.3\"><coordinates>1,2</coordinates></Point>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Point", DisplayName = "a sibling-version namespace refuses at the root — the namespace name is byte-exact, not family-shaped")]
    [DataRow("<Point xmlns=\"http://www.OpenGIS.net/kml/2.2\"><coordinates>1,2</coordinates></Point>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Point", DisplayName = "a case-variant namespace URI refuses at the root — namespace names compare literally")]
    [DataRow("<Point xmlns=\"http://earth.google.com/kml/2.1\"><coordinates>1,2</coordinates></Point>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Point", DisplayName = "a pre-OGC legacy namespace refuses at the root — no alias arm admits the earth.google.com family")]
    [DataRow("<gx:Track " + KmlTestDocuments.GxNamespaceDeclaration + "/>", (int)GeometryCodecRefusalKind.ProhibitedConstruct, "<gx:Track", DisplayName = "a vendor-extension root refuses by prohibition — the URI test precedes identity")]
    public void RootIdentityRefusals(string document, int kind, string marker)
    {
        KmlAssert.RefusesAt(document, (GeometryCodecRefusalKind)kind, marker);
    }

    /// <summary>The member run is a geometry-identity position: the model, features, foreign and absent namespaces refuse — no skip lane exists where a member would silently drop.</summary>
    [TestMethod]
    [DataRow("<Model/>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Model", DisplayName = "the textured model refuses at member position")]
    [DataRow("<Model><Location><longitude>1</longitude><latitude>2</latitude></Location><Link><href>models/un.dae</href></Link></Model>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Model", DisplayName = "a content-bearing textured model refuses at member position too")]
    [DataRow("<Placemark/>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<Placemark", DisplayName = "a feature element refuses at member position")]
    [DataRow("<x:Point xmlns:x=\"urn:example:profile\"/>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<x:Point", DisplayName = "a foreign-namespace member refuses instead of silently dropping")]
    [DataRow("<g:Point xmlns:g=\"http://earth.google.com/kml/2.1\"><g:coordinates>3,4</g:coordinates></g:Point>", (int)GeometryCodecRefusalKind.UnsupportedGeometry, "<g:Point", DisplayName = "a legacy-namespace member refuses at its own name — the pre-OGC family is not aliased")]
    [DataRow("<gx:Track " + KmlTestDocuments.GxNamespaceDeclaration + "/>", (int)GeometryCodecRefusalKind.ProhibitedConstruct, "<gx:Track", DisplayName = "a vendor-extension member refuses by prohibition")]
    public void MemberIdentityRefusals(string member, int kind, string marker)
    {
        string document = KmlTestDocuments.Root("MultiGeometry", "<Point><coordinates>1,2</coordinates></Point>" + member);
        KmlAssert.RefusesAt(document, (GeometryCodecRefusalKind)kind, marker);
    }

    /// <summary>A memberless aggregate refuses where the element ends — schema-valid but semantics-free.</summary>
    [TestMethod]
    public void TheMemberlessAggregateRefusesAtItsClose()
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root("MultiGeometry", ""), GeometryCodecRefusalKind.StructuralViolation, "</MultiGeometry>");
    }

    /// <summary>A memberless aggregate refuses at member position too — the no-empty-semantics rule is position-free.</summary>
    [TestMethod]
    public void TheMemberlessAggregateRefusesAtMemberPositionToo()
    {
        string document = KmlTestDocuments.Root("MultiGeometry", "<Point><coordinates>1,2</coordinates></Point><MultiGeometry></MultiGeometry>");
        KmlAssert.RefusesAt(document, GeometryCodecRefusalKind.StructuralViolation, "</MultiGeometry>");
    }

    /// <summary>A member in no namespace refuses at identity — the empty namespace is not the format's, at member position as at the root.</summary>
    [TestMethod]
    public void ANamespacelessMemberRefusesAtIdentity()
    {
        string document = KmlTestDocuments.PrefixedRoot("MultiGeometry", "<kml:Point><kml:coordinates>1,2</kml:coordinates></kml:Point><Point><coordinates>3,4</coordinates></Point>");
        KmlAssert.RefusesAt(document, GeometryCodecRefusalKind.UnsupportedGeometry, "<Point>");
    }

    /// <summary>The ring rules run at member position exactly as at the root — one leaf path, both positions.</summary>
    [TestMethod]
    public void TheRingRulesRunAtMemberPositionToo()
    {
        string document = KmlTestDocuments.Root("MultiGeometry", "<Point><coordinates>1,2</coordinates></Point><LinearRing><coordinates>0,0 4,0 4,4 1,1</coordinates></LinearRing>");
        KmlAssert.RefusesAt(document, GeometryCodecRefusalKind.StructuralViolation, "</coordinates></LinearRing>");
    }

    /// <summary>A vendor-extension element refuses by prohibition at content positions too.</summary>
    [TestMethod]
    public void AVendorExtensionChildRefusesInsideRecognizedContent()
    {
        string document = KmlTestDocuments.Root("LineString", "<gx:altitudeMode " + KmlTestDocuments.GxNamespaceDeclaration + ">clampToSeaFloor</gx:altitudeMode><coordinates>0,0 1,1</coordinates>");
        KmlAssert.RefusesAt(document, GeometryCodecRefusalKind.ProhibitedConstruct, "<gx:altitudeMode");
    }

    /// <summary>A no-namespace child inside a recognized element refuses — the content models carry no wildcard, and only namespaced foreign items are sanctioned.</summary>
    [TestMethod]
    public void ANamespacelessChildRefuses()
    {
        string document = KmlTestDocuments.PrefixedRoot("Point", "<coordinates>1,2</coordinates>");
        KmlAssert.RefusesAt(document, GeometryCodecRefusalKind.StructuralViolation, "<coordinates>");
    }

    /// <summary>The position taxonomy's three namespace arms hold at the polygon's own body and inside its boundaries, not only in a leaf body.</summary>
    [TestMethod]
    public void ThePositionTaxonomyHoldsAtPolygonAndBoundaryScope()
    {
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("Polygon", KmlTestDocuments.ForeignChild + KmlTestDocuments.SquarePolygonBody), "POLYGON ((0 0, 4 0, 4 4, 0 0))");
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("Polygon", "<outerBoundaryIs>" + KmlTestDocuments.ForeignChild + "<LinearRing>" + KmlTestDocuments.SquareRingCoordinates + "</LinearRing></outerBoundaryIs>"), "POLYGON ((0 0, 4 0, 4 4, 0 0))");

        KmlAssert.RefusesAt(KmlTestDocuments.Root("Polygon", "<gx:altitudeMode " + KmlTestDocuments.GxNamespaceDeclaration + ">clampToSeaFloor</gx:altitudeMode>" + KmlTestDocuments.SquarePolygonBody), GeometryCodecRefusalKind.ProhibitedConstruct, "<gx:altitudeMode");
        KmlAssert.RefusesAt(KmlTestDocuments.Root("Polygon", "<outerBoundaryIs><gx:LinearRing " + KmlTestDocuments.GxNamespaceDeclaration + "/><LinearRing>" + KmlTestDocuments.SquareRingCoordinates + "</LinearRing></outerBoundaryIs>"), GeometryCodecRefusalKind.ProhibitedConstruct, "<gx:LinearRing");

        string namespacelessAtPolygonScope = KmlTestDocuments.PrefixedRoot("Polygon", "<extrude>0</extrude><kml:outerBoundaryIs><kml:LinearRing><kml:coordinates>0,0 4,0 4,4 0,0</kml:coordinates></kml:LinearRing></kml:outerBoundaryIs>");
        KmlAssert.RefusesAt(namespacelessAtPolygonScope, GeometryCodecRefusalKind.StructuralViolation, "<extrude");
    }

    /// <summary>The content model is the schema's, exactly: unknown, duplicated, and out-of-order children refuse at their own names.</summary>
    [TestMethod]
    [DataRow("Point", "<tessellate>1</tessellate><coordinates>1,2</coordinates>", "<tessellate", DisplayName = "the point admits no draping flag")]
    [DataRow("Point", "<name>x</name><coordinates>1,2</coordinates>", "<name", DisplayName = "an unknown in-namespace child refuses")]
    [DataRow("LineString", "<extrude>0</extrude><extrude>1</extrude><coordinates>0,0 1,1</coordinates>", "<extrude>1", DisplayName = "a duplicated extrusion flag refuses at its second appearance")]
    [DataRow("LineString", "<coordinates>0,0 1,1</coordinates><altitudeMode>absolute</altitudeMode>", "<altitudeMode", DisplayName = "a mode behind the run is out of the schema's sequence")]
    [DataRow("LineString", "<altitudeMode>absolute</altitudeMode><extrude>1</extrude><coordinates>0,0 1,1</coordinates>", "<extrude", DisplayName = "an extrusion flag behind the mode is out of the schema's sequence")]
    [DataRow("LineString", "<coordinates>0,0 1,1</coordinates><coordinates>2,2 3,3</coordinates>", "<coordinates>2", DisplayName = "a duplicated run refuses at its second appearance")]
    [DataRow("LineString", "<altitudeMode>absolute</altitudeMode><altitudeMode>clampToGround</altitudeMode><coordinates>0,0 1,1</coordinates>", "<altitudeMode>clampToGround", DisplayName = "a duplicated mode refuses at its second appearance")]
    [DataRow("LineString", "<tessellate>1</tessellate><tessellate>0</tessellate><coordinates>0,0 1,1</coordinates>", "<tessellate>0", DisplayName = "a duplicated draping flag refuses at its second appearance")]
    [DataRow("Point", "<COORDINATES>1,2</COORDINATES>", "<COORDINATES", DisplayName = "a cased child name is not the schema's — the content model is case-sensitive")]
    [DataRow("Polygon", "<name>x</name>" + KmlTestDocuments.SquarePolygonBody, "<name", DisplayName = "an unknown in-namespace child in the polygon body refuses at its own name")]
    [DataRow("Point", "<Model/><coordinates>1,2</coordinates>", "<Model", DisplayName = "the textured model is an unknown in-namespace child inside a recognized element")]
    [DataRow("Polygon", "<outerBoundaryIs><Model/></outerBoundaryIs>", "<Model", DisplayName = "the textured model is a non-ring child inside a boundary property")]
    public void ContentModelViolationsRefuse(string localName, string body, string marker)
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root(localName, body), GeometryCodecRefusalKind.StructuralViolation, marker);
    }

    /// <summary>The altitude-mode token is schema-exact: padded, whitespace-only, unknown, and wrong-case tokens refuse at the content's first byte.</summary>
    [TestMethod]
    [DataRow("<altitudeMode> absolute </altitudeMode>", " absolute ", DisplayName = "a padded token is not an enumeration member")]
    [DataRow("<altitudeMode>   </altitudeMode>", "   </altitudeMode>", DisplayName = "whitespace-only content is neither empty nor a token")]
    [DataRow("<altitudeMode>clampToSeaFloor</altitudeMode>", "clampToSeaFloor", DisplayName = "an unrecognized token refuses")]
    [DataRow("<altitudeMode>ABSOLUTE</altitudeMode>", "ABSOLUTE", DisplayName = "the enumeration is case-sensitive")]
    public void AltitudeModeTokensAreSchemaExact(string mode, string marker)
    {
        string document = KmlTestDocuments.Root("LineString", mode + "<coordinates>0,0 1,1</coordinates>");
        KmlAssert.RefusesAt(document, GeometryCodecRefusalKind.StructuralViolation, marker);
    }

    /// <summary>The schema-exact token adjudication runs at the polygon's own mode slot too — the second call site.</summary>
    [TestMethod]
    public void ThePolygonScopeModeTokenIsSchemaExactToo()
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root("Polygon", "<altitudeMode> absolute </altitudeMode>" + KmlTestDocuments.SquarePolygonBody), GeometryCodecRefusalKind.StructuralViolation, " absolute ");

        KmlAssert.RefusesAt(KmlTestDocuments.Root("Polygon", "<altitudeMode>clampToSeaFloor</altitudeMode>" + KmlTestDocuments.SquarePolygonBody), GeometryCodecRefusalKind.StructuralViolation, "clampToSeaFloor");
    }

    /// <summary>Simple content admits no element in any namespace: one inside the run or the mode refuses as malformed at its own first byte.</summary>
    [TestMethod]
    [DataRow("<coordinates>1,2<x:b xmlns:x=\"urn:example:profile\"/></coordinates>", "<x:b", DisplayName = "an element after run text refuses — no run is spliced across it")]
    [DataRow("<coordinates><Point/></coordinates>", "<Point/>", DisplayName = "an element as the run's first content refuses")]
    [DataRow("<altitudeMode><x:b xmlns:x=\"urn:example:profile\"/></altitudeMode><coordinates>1,2</coordinates>", "<x:b", DisplayName = "an element inside the mode refuses")]
    [DataRow("<altitudeMode>absolute<x:b xmlns:x=\"urn:example:profile\"/></altitudeMode><coordinates>1,2</coordinates>", "<x:b", DisplayName = "an element after the mode's token refuses")]
    public void ElementsInsideSimpleContentRefuse(string body, string marker)
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root("Point", body), GeometryCodecRefusalKind.MalformedDocument, marker);
    }

    /// <summary>Character data where the content model expects elements refuses at its first non-whitespace byte.</summary>
    [TestMethod]
    [DataRow("Point", "hello<coordinates>1,2</coordinates>", "hello", DisplayName = "text inside the point body refuses")]
    [DataRow("MultiGeometry", "songline<Point><coordinates>1,2</coordinates></Point>", "songline", DisplayName = "text inside the member run refuses")]
    [DataRow("Polygon", "text" + KmlTestDocuments.SquarePolygonBody, "text", DisplayName = "text inside the polygon body refuses")]
    [DataRow("Point", "\n  hello<coordinates>1,2</coordinates>", "hello", DisplayName = "text behind ignorable whitespace refuses at its first non-whitespace byte, not at the token's start")]
    public void TextWhereElementsBelongRefuses(string localName, string body, string marker)
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root(localName, body), GeometryCodecRefusalKind.MalformedDocument, marker);
    }

    /// <summary>The coordinate run's presence rules: the absent element refuses where the geometry ends, empty and whitespace-only content where the run ends.</summary>
    [TestMethod]
    [DataRow("Point", "", "</Point>", DisplayName = "the absent run refuses at the geometry's close")]
    [DataRow("Point", "<coordinates></coordinates>", "</coordinates>", DisplayName = "the empty run refuses at its own close")]
    [DataRow("Point", "<coordinates>   </coordinates>", "</coordinates>", DisplayName = "the whitespace-only run refuses at its own close")]
    [DataRow("LineString", "", "</LineString>", DisplayName = "the absent run refuses at the line's close")]
    [DataRow("LinearRing", "", "</LinearRing>", DisplayName = "the absent run refuses at the ring's close")]
    [DataRow("LineString", "<coordinates></coordinates>", "</coordinates>", DisplayName = "the empty run under the line refuses at its own close")]
    [DataRow("LinearRing", "<coordinates></coordinates>", "</coordinates>", DisplayName = "the empty run under the ring refuses at its own close")]
    public void CoordinatePresenceRulesRefuse(string localName, string body, string marker)
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root(localName, body), GeometryCodecRefusalKind.StructuralViolation, marker);
    }

    /// <summary>The per-kind counts: a point carries exactly one tuple, a line two, a ring four closed — each shortfall where the count became final, the point's excess at the second tuple.</summary>
    [TestMethod]
    [DataRow("Point", "<coordinates>1,2 3,4</coordinates>", "3,4", DisplayName = "a second tuple under the point refuses at its own first byte")]
    [DataRow("LineString", "<coordinates>1,2</coordinates>", "</coordinates>", DisplayName = "a one-tuple line refuses where the run ends")]
    [DataRow("LinearRing", "<coordinates>0,0 4,0 0,0</coordinates>", "</coordinates>", DisplayName = "a three-tuple ring refuses where the run ends")]
    [DataRow("LinearRing", "<coordinates>0,0 4,0 4,4 1,1</coordinates>", "</coordinates>", DisplayName = "an unclosed ring refuses where the run ends")]
    [DataRow("Point", "<coordinates>1,2 3,4 5,6</coordinates>", "3,4", DisplayName = "a three-tuple point refuses at the second tuple, never at the last")]
    [DataRow("LinearRing", "<coordinates>1,1 4,1 4,4 1.0000000000000002,1</coordinates>", "</coordinates>", DisplayName = "a ring closing to within one unit in the last place is not closed — the predicate is exact")]
    [DataRow("LinearRing", "<coordinates>0,0 4,0 4,4 0,5</coordinates>", "</coordinates>", DisplayName = "a ring closing on the abscissa alone refuses where the run ends")]
    [DataRow("LinearRing", "<coordinates>0,0 4,0 4,4 5,0</coordinates>", "</coordinates>", DisplayName = "a ring closing on the ordinate alone refuses where the run ends")]
    public void PerKindCountRulesRefuse(string localName, string body, string marker)
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root(localName, body), GeometryCodecRefusalKind.StructuralViolation, marker);
    }

    /// <summary>Tuple component defects refuse at their pinned bytes: missing components at the byte where one was required, a fourth at its own first byte, shortfalls at the terminating byte.</summary>
    [TestMethod]
    [DataRow("<coordinates>,1,2</coordinates>", (int)GeometryCodecRefusalKind.MalformedDocument, ",1,2", DisplayName = "a leading comma stands where a component must begin")]
    [DataRow("<coordinates>,,,</coordinates>", (int)GeometryCodecRefusalKind.MalformedDocument, ",,,", DisplayName = "content of only commas refuses at the first")]
    [DataRow("<coordinates>1,,2</coordinates>", (int)GeometryCodecRefusalKind.MalformedDocument, ",2<", DisplayName = "an interior empty component refuses at the comma that stands in its place")]
    [DataRow("<coordinates>1,2 3,4,</coordinates>", (int)GeometryCodecRefusalKind.MalformedDocument, ",</coordinates>", DisplayName = "a trailing comma never gains its component")]
    [DataRow("<coordinates>1,2,3,7</coordinates>", (int)GeometryCodecRefusalKind.DimensionMismatch, "7", DisplayName = "a fourth component refuses at its own first byte")]
    [DataRow("<coordinates>5 1,2</coordinates>", (int)GeometryCodecRefusalKind.DimensionMismatch, " 1,2", DisplayName = "a one-component tuple refuses at its terminating byte")]
    [DataRow("<coordinates>1,2 5</coordinates>", (int)GeometryCodecRefusalKind.DimensionMismatch, "</coordinates>", DisplayName = "a one-component tuple at the run's end refuses where the run became final")]
    public void TupleComponentDefectsRefuse(string body, int kind, string marker)
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root("LineString", body), (GeometryCodecRefusalKind)kind, marker);
    }

    /// <summary>Arity commits at the leaf's first tuple; the first disagreeing tuple is the offender at its own first byte.</summary>
    [TestMethod]
    public void ArityCommitsAtTheFirstTuple()
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root("LineString", "<coordinates>1,2 3,4,5</coordinates>"), GeometryCodecRefusalKind.DimensionMismatch, "3,4,5");

        //The vendored suite's own artifact class: the dangling comma binds the following tuple into three components.
        KmlAssert.RefusesAt(KmlTestDocuments.Root("LinearRing", "<coordinates>-124.0,50.0 -124.0,46.0 -118.0,  -118.0,50.0 -124.0,50.0</coordinates>"), GeometryCodecRefusalKind.DimensionMismatch, "-118.0,  -118.0");
    }

    /// <summary>A ring both non-uniform and unclosed refuses the arity first — the offending tuple's byte precedes the run terminator.</summary>
    [TestMethod]
    public void TheDoublyDefectiveRingRefusesTheEarlierOffense()
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root("LinearRing", "<coordinates>0,0 4,0 4,4,9 1,1</coordinates>"), GeometryCodecRefusalKind.DimensionMismatch, "4,4,9");
    }

    /// <summary>The committed arity threads across a polygon's rings: a later ring disagreeing with the first refuses at its own first tuple.</summary>
    [TestMethod]
    public void PolygonRingsShareOneArity()
    {
        string body = KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs><LinearRing><coordinates>1,1,5 2,1,5 2,2,5 1,1,5</coordinates></LinearRing></innerBoundaryIs>";
        KmlAssert.RefusesAt(KmlTestDocuments.Root("Polygon", body), GeometryCodecRefusalKind.DimensionMismatch, "1,1,5");
    }

    /// <summary>Non-finite tuple components refuse as non-finite values — the spellings, the wider parsed family, and overflow — and lexical garbage as malformed.</summary>
    [TestMethod]
    [DataRow("NaN,2", (int)GeometryCodecRefusalKind.NonFiniteCoordinate, "NaN", DisplayName = "the not-a-number spelling")]
    [DataRow("1,INF", (int)GeometryCodecRefusalKind.NonFiniteCoordinate, "INF", DisplayName = "the infinity spelling")]
    [DataRow("1,-INF", (int)GeometryCodecRefusalKind.NonFiniteCoordinate, "-INF", DisplayName = "the negative infinity spelling")]
    [DataRow("Infinity,2", (int)GeometryCodecRefusalKind.NonFiniteCoordinate, "Infinity", DisplayName = "the parsed infinity family classifies alike")]
    [DataRow("1e999,2", (int)GeometryCodecRefusalKind.NonFiniteCoordinate, "1e999", DisplayName = "overflow of a syntactically finite token")]
    [DataRow("north,2", (int)GeometryCodecRefusalKind.MalformedDocument, "north", DisplayName = "lexical garbage is malformed")]
    [DataRow("nan,2", (int)GeometryCodecRefusalKind.NonFiniteCoordinate, "nan", DisplayName = "the lower-case not-a-number spelling classifies with the family")]
    [DataRow("NAN,2", (int)GeometryCodecRefusalKind.NonFiniteCoordinate, "NAN", DisplayName = "the upper-case not-a-number spelling classifies with the family")]
    [DataRow("+Infinity,2", (int)GeometryCodecRefusalKind.NonFiniteCoordinate, "+Infinity", DisplayName = "the signed infinity spelling classifies with the family")]
    [DataRow("1.5abc,2", (int)GeometryCodecRefusalKind.MalformedDocument, "1.5abc", DisplayName = "a numeric prefix behind garbage is malformed — a component consumes whole or not at all")]
    public void NonFiniteAndGarbageComponentsRefuse(string run, int kind, string marker)
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root("Point", $"<coordinates>{run}</coordinates>"), (GeometryCodecRefusalKind)kind, marker);
    }

    /// <summary>
    /// The boundary content model's ring-less interior form: the property is admitted and
    /// contributes no interior ring, while the exterior keeps requiring its ring.
    /// </summary>
    [TestMethod]
    [DataRow(KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs></innerBoundaryIs>", "POLYGON ((0 0, 4 0, 4 4, 0 0))", DisplayName = "a ring-less interior boundary contributes no hole")]
    [DataRow(KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs/>", "POLYGON ((0 0, 4 0, 4 4, 0 0))", DisplayName = "the self-closing interior spelling reads the same")]
    [DataRow(KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs></innerBoundaryIs><innerBoundaryIs><LinearRing>" + KmlTestDocuments.InnerRingCoordinates + "</LinearRing></innerBoundaryIs>", "POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", DisplayName = "a ring-less interior before a real one keeps the real hole")]
    [DataRow(KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs><LinearRing>" + KmlTestDocuments.InnerRingCoordinates + "</LinearRing></innerBoundaryIs><innerBoundaryIs></innerBoundaryIs>", "POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", DisplayName = "a ring-less interior behind a real one keeps the real hole")]
    [DataRow(KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs>" + KmlTestDocuments.ForeignChild + "</innerBoundaryIs>", "POLYGON ((0 0, 4 0, 4 4, 0 0))", DisplayName = "an interior boundary carrying only a foreign extension contributes no hole")]
    public void RinglessInteriorBoundariesContributeNoHole(string body, string wkt)
    {
        KmlAssert.MatchesWkt(KmlTestDocuments.Root("Polygon", body), wkt);
    }

    /// <summary>The polygon's boundary rules: a required exterior ring, interiors behind it, at most one ring per boundary.</summary>
    [TestMethod]
    [DataRow("", "</Polygon>", DisplayName = "a polygon without its exterior refuses where the element ends")]
    [DataRow("<innerBoundaryIs><LinearRing>" + KmlTestDocuments.InnerRingCoordinates + "</LinearRing></innerBoundaryIs>" + KmlTestDocuments.SquarePolygonBody, "<innerBoundaryIs", DisplayName = "an interior before the exterior refuses at its own name — the requirement is inevitable there")]
    [DataRow("<outerBoundaryIs></outerBoundaryIs>", "</outerBoundaryIs>", DisplayName = "an exterior boundary without its ring refuses where the boundary ends")]
    [DataRow("<outerBoundaryIs><LinearRing>" + KmlTestDocuments.SquareRingCoordinates + "</LinearRing><LinearRing><coordinates>5,5 6,5 6,6 5,5</coordinates></LinearRing></outerBoundaryIs>", "<LinearRing><coordinates>5,5", DisplayName = "a second ring in one boundary refuses at its own name")]
    [DataRow("<outerBoundaryIs><Point><coordinates>1,2</coordinates></Point></outerBoundaryIs>", "<Point", DisplayName = "a non-ring child in a boundary refuses at its own name")]
    [DataRow(KmlTestDocuments.SquarePolygonBody + "<innerBoundaryIs><Point><coordinates>1,2</coordinates></Point></innerBoundaryIs>", "<Point", DisplayName = "a non-ring child in an interior boundary refuses at its own name — only the ring-less form is admitted")]
    [DataRow(KmlTestDocuments.SquarePolygonBody + "<outerBoundaryIs><LinearRing><coordinates>5,5 6,5 6,6 5,5</coordinates></LinearRing></outerBoundaryIs>", "<outerBoundaryIs><LinearRing><coordinates>5,5", DisplayName = "a second exterior boundary refuses at its own name — the schema pins the singular")]
    [DataRow(KmlTestDocuments.SquarePolygonBody + "<altitudeMode>absolute</altitudeMode>", "<altitudeMode", DisplayName = "a presentation element behind a boundary is out of the schema's sequence")]
    [DataRow("<outerBoundaryIs><LinearRing></LinearRing></outerBoundaryIs>", "</LinearRing>", DisplayName = "a boundary ring without its run refuses at the ring's close")]
    public void PolygonBoundaryRulesRefuse(string body, string marker)
    {
        KmlAssert.RefusesAt(KmlTestDocuments.Root("Polygon", body), GeometryCodecRefusalKind.StructuralViolation, marker);
    }

    /// <summary>The security floor fires through the public reader: document type declarations and processing instructions refuse as prohibited.</summary>
    [TestMethod]
    public void TheSecurityFloorFiresThroughThePublicReader()
    {
        string doctype = "<!DOCTYPE kml [<!ENTITY x \"y\">]>" + KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates);
        KmlAssert.RefusesAt(doctype, GeometryCodecRefusalKind.ProhibitedConstruct, "<!DOCTYPE");

        string processingInstruction = KmlTestDocuments.Root("Point", "<?tuple 1?><coordinates>1,2</coordinates>");
        KmlAssert.RefusesAt(processingInstruction, GeometryCodecRefusalKind.ProhibitedConstruct, "<?tuple");
    }

    /// <summary>Transport is not content policy: a prohibited construct inside a wholesale-skipped subtree still refuses, on both skip lanes.</summary>
    [TestMethod]
    public void TheSecurityFloorFiresInsideASkippedSubtree()
    {
        string insideThePresentationFlag = KmlTestDocuments.Root("LineString", "<extrude><?pi 1?></extrude><coordinates>0,0 1,1</coordinates>");
        KmlAssert.RefusesAt(insideThePresentationFlag, GeometryCodecRefusalKind.ProhibitedConstruct, "<?pi");

        string insideTheForeignSubtree = KmlTestDocuments.Root("LineString", "<x:extra xmlns:x=\"urn:example:profile\">notes<?pi 1?></x:extra><coordinates>0,0 1,1</coordinates>");
        KmlAssert.RefusesAt(insideTheForeignSubtree, GeometryCodecRefusalKind.ProhibitedConstruct, "<?pi");

        string beforeAnyForeignText = KmlTestDocuments.Root("LineString", "<x:extra xmlns:x=\"urn:example:profile\"><?pi 1?></x:extra><coordinates>0,0 1,1</coordinates>");
        KmlAssert.RefusesAt(beforeAnyForeignText, GeometryCodecRefusalKind.ProhibitedConstruct, "<?pi");
    }

    /// <summary>Trailing content refuses at its first byte and truncation at the input length.</summary>
    [TestMethod]
    public void TrailingContentAndTruncationRefuse()
    {
        string trailing = KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates) + "junk";
        KmlAssert.RefusesAt(trailing, GeometryCodecRefusalKind.TrailingContent, "junk");

        string whole = KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates);
        string truncated = whole[..(whole.Length - 4)];
        KmlAssert.Refuses(truncated, GeometryCodecRefusalKind.MalformedDocument, Encoding.UTF8.GetByteCount(truncated));
    }

    /// <summary>A zero-length document refuses as malformed at byte zero — no empty value is minted where nothing was written.</summary>
    [TestMethod]
    public void TheZeroLengthDocumentRefusesAtByteZero()
    {
        KmlAssert.Refuses(string.Empty, GeometryCodecRefusalKind.MalformedDocument, 0);
    }

    /// <summary>A raw control byte inside run text refuses as malformed at its own byte through the public reader.</summary>
    [TestMethod]
    public void ARawControlByteRefusesAtItsOwnByte()
    {
        string document = KmlTestDocuments.Root("Point", "<coordinates>1,\u00012</coordinates>");
        KmlAssert.RefusesAt(document, GeometryCodecRefusalKind.MalformedDocument, "\u0001");
    }

    /// <summary>Mixed-defect documents adjudicate the first offense in document order.</summary>
    [TestMethod]
    public void MixedDefectsAdjudicateInDocumentOrder()
    {
        string prohibitionFirst = "<gx:Track " + KmlTestDocuments.GxNamespaceDeclaration + "/>" + "junk";
        KmlAssert.RefusesAt(prohibitionFirst, GeometryCodecRefusalKind.ProhibitedConstruct, "<gx:Track");

        string modeBeforeBadRun = KmlTestDocuments.Root("LineString", "<altitudeMode>seafloor</altitudeMode><coordinates>north,2</coordinates>");
        KmlAssert.RefusesAt(modeBeforeBadRun, GeometryCodecRefusalKind.StructuralViolation, "seafloor");

        string memberlessBeforeTrailing = KmlTestDocuments.Root("MultiGeometry", "") + "junk";
        KmlAssert.RefusesAt(memberlessBeforeTrailing, GeometryCodecRefusalKind.StructuralViolation, "</MultiGeometry>");
    }

    /// <summary>A defect after the geometry value completes still rents nothing — materialization is deferred past the whole document.</summary>
    [TestMethod]
    public void ARefusalAfterACleanGeometryRentsNothing()
    {
        string document = KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates) + "junk";
        CountingAllocatorSpy spy = new();
        FlatGeometryAllocators allocators = new(spy.RentVertexColumn, spy.RentOrdinateColumn);
        bool accepted = KmlGeometryReader.TryRead(Encoding.UTF8.GetBytes(document), allocators, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "the trailing content must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.TrailingContent, refusal.Kind, "the post-value defect is the refusal");
        Assert.AreEqual(0, spy.RentalCount, "a refused read rents nothing");
    }

    /// <summary>The character-span convenience carries the caller's allocator seam through to materialization — it is not the default seam in disguise.</summary>
    [TestMethod]
    public void TheCharacterOverloadCarriesTheCallersAllocatorSeam()
    {
        string document = KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates);
        CountingAllocatorSpy spy = new();
        FlatGeometryAllocators allocators = new(spy.RentVertexColumn, spy.RentOrdinateColumn);
        bool accepted = KmlGeometryReader.TryRead(document.AsSpan(), allocators, out FlatGeometry geometry, out GeometryCodecRefusal refusal);

        Assert.IsTrue(accepted, $"the character span must be accepted, but refused {refusal.Kind} at {refusal.ByteOffset}");

        using(geometry)
        {
            Assert.IsGreaterThan(0, spy.RentalCount, "the character path rents through the caller's seam, not the default one");
        }
    }

    /// <summary>
    /// A counting allocator seam: heap-backed columns whose rental count the
    /// rent-late rows assert.
    /// </summary>
    private sealed class CountingAllocatorSpy
    {
        /// <summary>The number of column rentals taken through the seam.</summary>
        public int RentalCount { get; private set; }

        /// <summary>Rents a heap-backed vertex column and counts it.</summary>
        public System.Buffers.IMemoryOwner<Point2d> RentVertexColumn(int length)
        {
            RentalCount++;

            return FlatGeometryAllocators.Default.VertexColumns(length);
        }

        /// <summary>Rents a heap-backed ordinate column and counts it.</summary>
        public System.Buffers.IMemoryOwner<double> RentOrdinateColumn(int length)
        {
            RentalCount++;

            return FlatGeometryAllocators.Default.OrdinateColumns(length);
        }
    }
}
