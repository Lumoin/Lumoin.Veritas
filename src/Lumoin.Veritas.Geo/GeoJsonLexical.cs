using System;

using Lumoin.Veritas.Geo.Json;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// A span recognizer for the lexical shape of a GeoJSON geometry literal body: one JSON object whose
/// <c>type</c> member names a geometry, carrying either the <c>coordinates</c> nesting that type fixes or
/// the <c>geometries</c> array of a geometry collection. The scan is one forward pass over an explicit
/// frame stack bounded by <see cref="MaximumNestingDepth"/>, with no recursion and no runtime regular
/// expressions.
/// </summary>
/// <remarks>
/// <para>
/// The JSON token grammar is certified exactly: a number carries no leading plus, no leading decimal
/// point and no leading zeros; a string validates every escape including <c>\uXXXX</c>; an unescaped byte
/// below <c>0x20</c> inside a string, a trailing comma, and any content after the single top-level value
/// are malformed. The body is the literal's UTF-8 bytes, so multi-byte sequences inside strings pass
/// through untranscoded and a <c>\uXXXX</c> escape is certified as four hexadecimal digits without being
/// paired into a surrogate.
/// </para>
/// <para>
/// Certification of one object is order-independent across its members: the tracked members are recorded
/// as they are met and the object is decided at its closing brace, so <c>type</c> may follow
/// <c>coordinates</c>. Foreign members are legal — their values are scanned for JSON validity on the same
/// frame stack and certify nothing. The specification defines a foreign member by its name being
/// undescribed, so <c>type</c>, <c>coordinates</c>, <c>geometries</c> and <c>bbox</c> are never foreign:
/// their described shapes are certified wherever they appear in a geometry object, whatever the object's
/// type turns out to be.
/// </para>
/// <para>
/// The abstention set is: a member name or a <c>type</c> value written with a backslash escape, whose
/// decoded spelling is a claim this recognizer does not make; a repeated tracked member in one object,
/// where the grammar does not fix which occurrence binds; and a <c>crs</c> member, a key the specification
/// removed, in the shapes the paragraph below leaves standing. Each of these leaves the body uncertified
/// rather than condemned, so the answer is <see cref="GeometryLexicalRecognition.Unrecognized"/>.
/// Abstention takes effect where it is met: a violation proved earlier in the one forward pass stands.
/// </para>
/// <para>
/// A <c>crs</c> member of a nested geometry object carries no adjudicated form. A <c>crs</c> member of the
/// top-level object carries exactly one: the object whose <c>type</c> is <c>name</c> and whose
/// <c>properties</c> name the legacy CRS84 identifier, the system this format fixes anyway, so that form
/// asserts nothing beyond the default and abstains with the key's other uses. Every other value there is
/// malformed — <c>null</c>, a string, an array, a number, and an object naming another system or written in
/// another form. Foreign members stand inside the <c>crs</c> object and inside its <c>properties</c> object
/// as they do everywhere else, the last occurrence of a recognized member binding, and the member names and
/// the identifier compared there are the format vocabulary's own constants.
/// </para>
/// <para>
/// A <c>coordinates</c> value is an array, and below it only arrays and numbers appear: an array holding
/// a number holds nothing else and is a position leaf, an array holding an array holds nothing else, and
/// any other value there is malformed. A <c>bbox</c> value is a flat array of numbers.
/// </para>
/// <para>
/// The recognizer is lexical: it certifies token shape and nesting structure, not geometry semantics.
/// Position element counts beyond the two the grammar fixes, ring closure, minimum ring lengths and the
/// match between a bounding box length and a coordinate dimension are semantics and are not enforced, so
/// no form the specification admits is ever rejected. Empty arrays are admitted at every level of a
/// <c>coordinates</c> value; they leave the leaf position depth unmeasured, and an unmeasured depth
/// claims nothing against the type.
/// </para>
/// </remarks>
public static class GeoJsonLexical
{
    /// <summary>
    /// The hard cap on JSON container nesting depth, counting the top-level object as the first level. The
    /// cap carries the thirty-two-level geometry nesting bound the readers of this format certify, measured
    /// in the containers this scan counts: a geometry nested that far spells thirty-one wrapping
    /// collections, each costing the collection object and its <c>geometries</c> array — sixty-two
    /// containers — plus the innermost geometry's own object and the four array levels a multipolygon's
    /// coordinates reach, sixty-seven in all, so no body inside the geometry bound is ever answered on depth
    /// here. A body needing more open containers than this answers
    /// <see cref="GeometryLexicalRecognition.DepthExceeded"/> instead of being scanned further.
    /// </summary>
    public const int MaximumNestingDepth = 96;

    /// <summary>The role one open JSON container plays in the scan.</summary>
    private enum FrameKind : byte
    {
        /// <summary>An object certified as a geometry object.</summary>
        GeometryObject,

        /// <summary>An object inside an uncertified value, where only JSON validity is checked.</summary>
        ForeignObject,

        /// <summary>An array inside an uncertified value, where only JSON validity is checked.</summary>
        ForeignArray,

        /// <summary>An array inside a <c>coordinates</c> value: arrays or numbers, never both.</summary>
        CoordinatesArray,

        /// <summary>A <c>geometries</c> array, whose every element is an object certified as a geometry object.</summary>
        GeometriesArray,

        /// <summary>A <c>bbox</c> array: numbers only, in an even count.</summary>
        BoundingBoxArray,

        /// <summary>
        /// The top-level <c>crs</c> member's object, decided against the one recognized form at its closing
        /// brace.
        /// </summary>
        CoordinateReferenceObject,

        /// <summary>The <c>properties</c> object of a <c>crs</c> object, which carries the system's name.</summary>
        CoordinateReferenceProperties
    }

    /// <summary>What the caller does with a <c>crs</c> member's value once the member has been classified.</summary>
    private enum CoordinateReferenceMemberOutcome : byte
    {
        /// <summary>The value is still ahead of the scan and certifies nothing, so it is read as a foreign value.</summary>
        ValueUnread,

        /// <summary>The value was read in place, so the scan resumes at the object's next member.</summary>
        ValueRead,

        /// <summary>The value opens the <c>properties</c> object, whose frame the caller pushes.</summary>
        PropertiesObject,

        /// <summary>The value opens a string the grammar never closes.</summary>
        Invalid
    }

    /// <summary>The members of a geometry object whose presence the scan tracks.</summary>
    [Flags]
    private enum TrackedMembers : byte
    {
        /// <summary>No tracked member; a foreign member falls here and certifies nothing.</summary>
        None = 0,

        /// <summary>The <c>type</c> member.</summary>
        Type = 1,

        /// <summary>The <c>coordinates</c> member.</summary>
        Coordinates = 2,

        /// <summary>The <c>geometries</c> member.</summary>
        Geometries = 4,

        /// <summary>The <c>bbox</c> member.</summary>
        Bbox = 8,
    }

    /// <summary>The geometry named by a <c>type</c> member.</summary>
    private enum GeometryTypeName : byte
    {
        /// <summary>No certified type value has been read for the object.</summary>
        None = 0,

        /// <summary>The <c>Point</c> type, whose coordinates are one position.</summary>
        Point,

        /// <summary>The <c>MultiPoint</c> type, whose coordinates are an array of positions.</summary>
        MultiPoint,

        /// <summary>The <c>LineString</c> type, whose coordinates are an array of positions.</summary>
        LineString,

        /// <summary>The <c>MultiLineString</c> type, whose coordinates are an array of arrays of positions.</summary>
        MultiLineString,

        /// <summary>The <c>Polygon</c> type, whose coordinates are an array of arrays of positions.</summary>
        Polygon,

        /// <summary>The <c>MultiPolygon</c> type, whose coordinates nest one level deeper than a polygon's.</summary>
        MultiPolygon,

        /// <summary>The <c>GeometryCollection</c> type, which carries <c>geometries</c> instead of coordinates.</summary>
        GeometryCollection,
    }

    /// <summary>The outcome of consuming one JSON value in an uncertified position.</summary>
    private enum ValueOutcome : byte
    {
        /// <summary>The value was read in place, or its container frame was pushed.</summary>
        Consumed,

        /// <summary>The bytes at the position do not begin a well-formed JSON value.</summary>
        Invalid,

        /// <summary>The value is a container that would pass the nesting cap.</summary>
        TooDeep,
    }

    /// <summary>The scan state of one open JSON container.</summary>
    private struct Frame
    {
        /// <summary>The role this container plays.</summary>
        public FrameKind Kind;

        /// <summary>Whether an item has already been consumed at this level.</summary>
        public bool HasItem;

        /// <summary>Whether a comma was consumed at this level, so another item is required before the close.</summary>
        public bool NeedItem;

        /// <summary>
        /// Whether this geometry object is still claimable. It is cleared by an escaped member name, an
        /// escaped <c>type</c> value, or a repeated tracked member; from that point the object certifies
        /// nothing and its remaining tracked members are scanned as foreign values.
        /// </summary>
        public bool Certified;

        /// <summary>The tracked members met so far in this geometry object.</summary>
        public TrackedMembers SeenMembers;

        /// <summary>The geometry named by this object's first <c>type</c> member.</summary>
        public GeometryTypeName TypeValue;

        /// <summary>
        /// The depth, relative to this object's <c>coordinates</c> value, at which a position leaf was
        /// measured; zero while the value carries no number-bearing leaf.
        /// </summary>
        public int CoordinatesLeafDepth;

        /// <summary>
        /// The depth of this array within the <c>coordinates</c> value holding it, counting the value's own
        /// array as one.
        /// </summary>
        public int ItemDepth;

        /// <summary>The frame index of the geometry object owning the <c>coordinates</c> value this array belongs to.</summary>
        public int OwnerIndex;

        /// <summary>How many numbers this array holds directly.</summary>
        public int NumberCount;

        /// <summary>How many arrays this array holds directly.</summary>
        public int ArrayCount;

        /// <summary>
        /// Whether this <c>crs</c> object's <c>type</c> member named the recognized form. The last
        /// occurrence in one object binds.
        /// </summary>
        public bool CoordinateReferenceTypeMatched;

        /// <summary>
        /// Whether the legacy CRS84 identifier was met: recorded by a <c>properties</c> object's frame for
        /// its own <c>name</c> member, and carried to the <c>crs</c> frame when that object closes.
        /// </summary>
        public bool CoordinateReferenceNameMatched;
    }

    /// <summary>Lexically recognizes one GeoJSON geometry literal body.</summary>
    /// <param name="body">The candidate GeoJSON text as UTF-8 bytes.</param>
    /// <returns>The recognition outcome; an empty or all-whitespace body is well-formed (an empty geometry).</returns>
    public static GeometryLexicalRecognition Recognize(ReadOnlySpan<byte> body)
    {
        int index = 0;
        SkipWhitespace(body, ref index);
        if(index == body.Length)
        {
            return GeometryLexicalRecognition.WellFormed;
        }

        //The body is one geometry object, so every other JSON value is outside the grammar.
        if(body[index] != (byte)'{')
        {
            return GeometryLexicalRecognition.Malformed;
        }

        Span<Frame> frames = stackalloc Frame[MaximumNestingDepth];
        frames[0] = new Frame { Kind = FrameKind.GeometryObject, Certified = true };
        int depth = 1;
        index++;
        bool uncertified = false;

        while(depth > 0)
        {
            SkipWhitespace(body, ref index);
            if(index == body.Length)
            {
                return GeometryLexicalRecognition.Malformed;
            }

            byte current = body[index];
            ref Frame top = ref frames[depth - 1];
            bool objectFrame = IsObjectFrame(top.Kind);

            if(current == (objectFrame ? (byte)'}' : (byte)']'))
            {
                if(top.NeedItem)
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                switch(top.Kind)
                {
                    case(FrameKind.GeometryObject):
                    {
                        if(top.Certified && !IsCertifiedGeometryObject(in top))
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        break;
                    }

                    case(FrameKind.CoordinatesArray):
                    {
                        if(top.NumberCount > 0)
                        {
                            //An array holding numbers is a position, which the grammar fixes at two or more.
                            if(top.NumberCount < 2)
                            {
                                return GeometryLexicalRecognition.Malformed;
                            }

                            ref Frame owner = ref frames[top.OwnerIndex];
                            if(owner.Certified)
                            {
                                if(owner.CoordinatesLeafDepth == 0)
                                {
                                    owner.CoordinatesLeafDepth = top.ItemDepth;
                                }
                                else if(owner.CoordinatesLeafDepth != top.ItemDepth)
                                {
                                    return GeometryLexicalRecognition.Malformed;
                                }
                            }
                        }

                        break;
                    }

                    case(FrameKind.BoundingBoxArray):
                    {
                        //A bounding box holds two values per dimension, so an odd count is outside the grammar.
                        if((top.NumberCount & 1) != 0)
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        break;
                    }

                    case(FrameKind.CoordinateReferenceObject):
                    {
                        //The recognized form names itself and carries the legacy CRS84 identifier; a frame
                        //whose spellings went unread claims nothing in either direction.
                        if(top.Certified && !(top.CoordinateReferenceTypeMatched && top.CoordinateReferenceNameMatched))
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        break;
                    }

                    case(FrameKind.CoordinateReferenceProperties):
                    {
                        ref Frame crsObject = ref frames[top.OwnerIndex];
                        crsObject.CoordinateReferenceNameMatched = top.CoordinateReferenceNameMatched;
                        if(!top.Certified)
                        {
                            crsObject.Certified = false;
                        }

                        break;
                    }

                    default:
                    {
                        break;
                    }
                }

                index++;
                depth--;
                continue;
            }

            if(current == (byte)',')
            {
                if(!top.HasItem || top.NeedItem)
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                top.NeedItem = true;
                index++;
                continue;
            }

            //Two items in a row without the separating comma.
            if(top.HasItem && !top.NeedItem)
            {
                return GeometryLexicalRecognition.Malformed;
            }

            if(objectFrame)
            {
                if(current != (byte)'"')
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                if(!TryScanString(body, ref index, out int nameStart, out int nameLength, out bool nameHasEscape))
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                SkipWhitespace(body, ref index);
                if(index == body.Length || body[index] != (byte)':')
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                index++;
                SkipWhitespace(body, ref index);
                if(index == body.Length)
                {
                    return GeometryLexicalRecognition.Malformed;
                }

                top.HasItem = true;
                top.NeedItem = false;

                TrackedMembers member = TrackedMembers.None;
                bool coordinateReferenceValue = false;
                if(top.Kind == FrameKind.GeometryObject)
                {
                    ReadOnlySpan<byte> name = body.Slice(nameStart, nameLength);
                    if(nameHasEscape)
                    {
                        top.Certified = false;
                        uncertified = true;
                    }
                    else if(name.SequenceEqual(GeoJsonVocabulary.CrsMemberName))
                    {
                        //The top-level key carries one recognized value, whose shape its own frames
                        //adjudicate; the key on a nested object claims nothing in either direction.
                        coordinateReferenceValue = depth == 1;
                        uncertified = true;
                    }
                    else if(top.Certified)
                    {
                        member = ClassifyTrackedMember(name);
                        if(member != TrackedMembers.None && (top.SeenMembers & member) != TrackedMembers.None)
                        {
                            top.Certified = false;
                            uncertified = true;
                            member = TrackedMembers.None;
                        }
                        else
                        {
                            top.SeenMembers |= member;
                        }
                    }
                }
                else if(top.Kind is FrameKind.CoordinateReferenceObject or FrameKind.CoordinateReferenceProperties)
                {
                    CoordinateReferenceMemberOutcome coordinateReferenceMember = ClassifyCoordinateReferenceMember(body, ref index, ref top, body.Slice(nameStart, nameLength), nameHasEscape);

                    switch(coordinateReferenceMember)
                    {
                        case(CoordinateReferenceMemberOutcome.Invalid):
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        case(CoordinateReferenceMemberOutcome.ValueRead):
                        {
                            continue;
                        }

                        case(CoordinateReferenceMemberOutcome.PropertiesObject):
                        {
                            if(depth == MaximumNestingDepth)
                            {
                                return GeometryLexicalRecognition.DepthExceeded;
                            }

                            frames[depth] = new Frame { Kind = FrameKind.CoordinateReferenceProperties, Certified = true, OwnerIndex = depth - 1 };
                            depth++;
                            index++;

                            continue;
                        }

                        default:
                        {
                            break;
                        }
                    }
                }

                if(coordinateReferenceValue)
                {
                    //Only an object can carry the recognized form, so a null, a string, an array and a
                    //number are outside it here; an object naming something else is decided at its close.
                    if(body[index] != (byte)'{')
                    {
                        return GeometryLexicalRecognition.Malformed;
                    }

                    if(depth == MaximumNestingDepth)
                    {
                        return GeometryLexicalRecognition.DepthExceeded;
                    }

                    frames[depth] = new Frame { Kind = FrameKind.CoordinateReferenceObject, Certified = true };
                    depth++;
                    index++;

                    continue;
                }

                switch(member)
                {
                    case(TrackedMembers.Type):
                    {
                        if(body[index] != (byte)'"')
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        if(!TryScanString(body, ref index, out int valueStart, out int valueLength, out bool valueHasEscape))
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        if(valueHasEscape)
                        {
                            top.Certified = false;
                            uncertified = true;
                            continue;
                        }

                        if(!TryClassifyGeometryType(body.Slice(valueStart, valueLength), out GeometryTypeName typeName))
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        top.TypeValue = typeName;
                        continue;
                    }

                    case(TrackedMembers.Coordinates):
                    case(TrackedMembers.Geometries):
                    case(TrackedMembers.Bbox):
                    {
                        if(body[index] != (byte)'[')
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        if(depth == MaximumNestingDepth)
                        {
                            return GeometryLexicalRecognition.DepthExceeded;
                        }

                        FrameKind valueKind = member switch
                        {
                            TrackedMembers.Coordinates => FrameKind.CoordinatesArray,
                            TrackedMembers.Geometries => FrameKind.GeometriesArray,
                            _ => FrameKind.BoundingBoxArray
                        };

                        frames[depth] = new Frame { Kind = valueKind, ItemDepth = 1, OwnerIndex = depth - 1 };
                        depth++;
                        index++;
                        continue;
                    }

                    default:
                    {
                        ValueOutcome outcome = TryBeginForeignValue(body, ref index, frames, ref depth);
                        if(outcome == ValueOutcome.Invalid)
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        if(outcome == ValueOutcome.TooDeep)
                        {
                            return GeometryLexicalRecognition.DepthExceeded;
                        }

                        continue;
                    }
                }
            }

            switch(top.Kind)
            {
                case(FrameKind.CoordinatesArray):
                {
                    if(current == (byte)'[')
                    {
                        //An array holding a number holds nothing else.
                        if(top.NumberCount > 0)
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        if(depth == MaximumNestingDepth)
                        {
                            return GeometryLexicalRecognition.DepthExceeded;
                        }

                        top.ArrayCount++;
                        top.HasItem = true;
                        top.NeedItem = false;
                        frames[depth] = new Frame { Kind = FrameKind.CoordinatesArray, ItemDepth = top.ItemDepth + 1, OwnerIndex = top.OwnerIndex };
                        depth++;
                        index++;
                        continue;
                    }

                    if(IsNumberStart(current))
                    {
                        //An array holding an array holds nothing else.
                        if(top.ArrayCount > 0)
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        if(!TryReadNumber(body, ref index))
                        {
                            return GeometryLexicalRecognition.Malformed;
                        }

                        top.NumberCount++;
                        top.HasItem = true;
                        top.NeedItem = false;
                        continue;
                    }

                    return GeometryLexicalRecognition.Malformed;
                }

                case(FrameKind.GeometriesArray):
                {
                    if(current != (byte)'{')
                    {
                        return GeometryLexicalRecognition.Malformed;
                    }

                    if(depth == MaximumNestingDepth)
                    {
                        return GeometryLexicalRecognition.DepthExceeded;
                    }

                    top.HasItem = true;
                    top.NeedItem = false;
                    frames[depth] = new Frame { Kind = FrameKind.GeometryObject, Certified = true };
                    depth++;
                    index++;
                    continue;
                }

                case(FrameKind.BoundingBoxArray):
                {
                    if(!IsNumberStart(current) || !TryReadNumber(body, ref index))
                    {
                        return GeometryLexicalRecognition.Malformed;
                    }

                    top.NumberCount++;
                    top.HasItem = true;
                    top.NeedItem = false;
                    continue;
                }

                case(FrameKind.ForeignArray):
                default:
                {
                    top.HasItem = true;
                    top.NeedItem = false;

                    ValueOutcome outcome = TryBeginForeignValue(body, ref index, frames, ref depth);
                    if(outcome == ValueOutcome.Invalid)
                    {
                        return GeometryLexicalRecognition.Malformed;
                    }

                    if(outcome == ValueOutcome.TooDeep)
                    {
                        return GeometryLexicalRecognition.DepthExceeded;
                    }

                    continue;
                }
            }
        }

        SkipWhitespace(body, ref index);
        if(index != body.Length)
        {
            return GeometryLexicalRecognition.Malformed;
        }

        return uncertified ? GeometryLexicalRecognition.Unrecognized : GeometryLexicalRecognition.WellFormed;
    }

    /// <summary>
    /// Decides one geometry object at its closing brace: the object names a type, carries the member that
    /// type requires, and — when its coordinates value held a number-bearing leaf — measured that leaf at
    /// the depth the type fixes.
    /// </summary>
    /// <param name="frame">The geometry object's frame, with its tracked-member state accumulated.</param>
    /// <returns><see langword="true"/> when the object is a geometry object of the named type.</returns>
    private static bool IsCertifiedGeometryObject(in Frame frame)
    {
        if((frame.SeenMembers & TrackedMembers.Type) == TrackedMembers.None)
        {
            return false;
        }

        if(frame.TypeValue == GeometryTypeName.GeometryCollection)
        {
            return (frame.SeenMembers & TrackedMembers.Geometries) != TrackedMembers.None;
        }

        if((frame.SeenMembers & TrackedMembers.Coordinates) == TrackedMembers.None)
        {
            return false;
        }

        return frame.CoordinatesLeafDepth == 0 || frame.CoordinatesLeafDepth == RequiredLeafDepth(frame.TypeValue);
    }

    /// <summary>The nesting depth, within a <c>coordinates</c> value, at which the type places its positions.</summary>
    /// <param name="typeName">The geometry named by the object's <c>type</c> member.</param>
    /// <returns>The leaf position depth, or zero for a type that carries no coordinates.</returns>
    private static int RequiredLeafDepth(GeometryTypeName typeName)
    {
        return typeName switch
        {
            GeometryTypeName.Point => 1,
            GeometryTypeName.MultiPoint => 2,
            GeometryTypeName.LineString => 2,
            GeometryTypeName.MultiLineString => 3,
            GeometryTypeName.Polygon => 3,
            GeometryTypeName.MultiPolygon => 4,
            _ => 0
        };
    }

    /// <summary>
    /// Consumes one JSON value in a position that certifies nothing: a scalar is read in place, and an
    /// object or array pushes a frame onto the shared stack under the shared cap.
    /// </summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, advanced past a scalar or past the container's opening byte.</param>
    /// <param name="frames">The shared frame stack.</param>
    /// <param name="depth">The number of open frames, incremented when a container is pushed.</param>
    /// <returns>Whether the value was consumed, is invalid, or would pass the nesting cap.</returns>
    private static ValueOutcome TryBeginForeignValue(ReadOnlySpan<byte> body, ref int index, Span<Frame> frames, ref int depth)
    {
        byte current = body[index];
        if(current is (byte)'{' or (byte)'[')
        {
            if(depth == MaximumNestingDepth)
            {
                return ValueOutcome.TooDeep;
            }

            frames[depth] = new Frame { Kind = current == (byte)'{' ? FrameKind.ForeignObject : FrameKind.ForeignArray };
            depth++;
            index++;

            return ValueOutcome.Consumed;
        }

        if(current == (byte)'"')
        {
            return TryScanString(body, ref index, out _, out _, out _) ? ValueOutcome.Consumed : ValueOutcome.Invalid;
        }

        if(IsNumberStart(current))
        {
            return TryReadNumber(body, ref index) ? ValueOutcome.Consumed : ValueOutcome.Invalid;
        }

        return current switch
        {
            (byte)'t' => TryReadKeyword(body, ref index, "true"u8) ? ValueOutcome.Consumed : ValueOutcome.Invalid,
            (byte)'f' => TryReadKeyword(body, ref index, "false"u8) ? ValueOutcome.Consumed : ValueOutcome.Invalid,
            (byte)'n' => TryReadKeyword(body, ref index, "null"u8) ? ValueOutcome.Consumed : ValueOutcome.Invalid,
            _ => ValueOutcome.Invalid
        };
    }

    /// <summary>
    /// Adjudicates one member of the top-level <c>crs</c> object or of that object's <c>properties</c>
    /// object against the one recognized form, reading the member's value in place when the form fixes that
    /// value as a string. A member name or a tracked value written with a backslash escape clears the
    /// frame's claimability, because the decoded spelling is one this recognizer does not read.
    /// </summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, at the member's value, advanced past a value read here.</param>
    /// <param name="frame">The <c>crs</c> object's frame, or its <c>properties</c> object's frame.</param>
    /// <param name="name">The member name's raw content bytes, without the enclosing quotes.</param>
    /// <param name="nameHasEscape">Whether the member name carries a backslash escape.</param>
    /// <returns>What the caller does next with the member's value.</returns>
    private static CoordinateReferenceMemberOutcome ClassifyCoordinateReferenceMember(ReadOnlySpan<byte> body, ref int index, ref Frame frame, ReadOnlySpan<byte> name, bool nameHasEscape)
    {
        if(nameHasEscape)
        {
            frame.Certified = false;

            return CoordinateReferenceMemberOutcome.ValueUnread;
        }

        bool crsObject = frame.Kind == FrameKind.CoordinateReferenceObject;
        if(crsObject && name.SequenceEqual(GeoJsonVocabulary.PropertiesMemberName))
        {
            //A properties value that is no object carries no name and leaves the name state where the
            //object's other members put it.
            return body[index] == (byte)'{'
                ? CoordinateReferenceMemberOutcome.PropertiesObject
                : CoordinateReferenceMemberOutcome.ValueUnread;
        }

        ReadOnlySpan<byte> trackedName = crsObject ? GeoJsonVocabulary.TypeMemberName : GeoJsonVocabulary.CrsNameMemberName;
        if(!name.SequenceEqual(trackedName))
        {
            return CoordinateReferenceMemberOutcome.ValueUnread;
        }

        if(body[index] != (byte)'"')
        {
            SetCoordinateReferenceMatch(ref frame, crsObject, matched: false);

            return CoordinateReferenceMemberOutcome.ValueUnread;
        }

        if(!TryScanString(body, ref index, out int valueStart, out int valueLength, out bool valueHasEscape))
        {
            return CoordinateReferenceMemberOutcome.Invalid;
        }

        if(valueHasEscape)
        {
            frame.Certified = false;

            return CoordinateReferenceMemberOutcome.ValueRead;
        }

        ReadOnlySpan<byte> trackedValue = crsObject ? GeoJsonVocabulary.CrsNameFormValue : GeoJsonVocabulary.LegacyCrs84Name;
        SetCoordinateReferenceMatch(ref frame, crsObject, body.Slice(valueStart, valueLength).SequenceEqual(trackedValue));

        return CoordinateReferenceMemberOutcome.ValueRead;
    }

    /// <summary>
    /// Records the match of the one tracked member of a <c>crs</c> frame; the last occurrence in one object
    /// binds, so a repeated member overwrites what an earlier one recorded.
    /// </summary>
    /// <param name="frame">The frame recording the match.</param>
    /// <param name="crsObject">Whether the frame is the <c>crs</c> object rather than its <c>properties</c> object.</param>
    /// <param name="matched">Whether the member's value is the one the form fixes.</param>
    private static void SetCoordinateReferenceMatch(ref Frame frame, bool crsObject, bool matched)
    {
        if(crsObject)
        {
            frame.CoordinateReferenceTypeMatched = matched;
        }
        else
        {
            frame.CoordinateReferenceNameMatched = matched;
        }
    }

    /// <summary>Maps a member name to the tracked member it names, comparing bytes case-sensitively.</summary>
    /// <param name="name">The member name's raw content bytes, without the enclosing quotes.</param>
    /// <returns>The tracked member, or <see cref="TrackedMembers.None"/> for a foreign member.</returns>
    private static TrackedMembers ClassifyTrackedMember(ReadOnlySpan<byte> name)
    {
        if(name.SequenceEqual("type"u8))
        {
            return TrackedMembers.Type;
        }

        if(name.SequenceEqual("coordinates"u8))
        {
            return TrackedMembers.Coordinates;
        }

        if(name.SequenceEqual("geometries"u8))
        {
            return TrackedMembers.Geometries;
        }

        if(name.SequenceEqual("bbox"u8))
        {
            return TrackedMembers.Bbox;
        }

        return TrackedMembers.None;
    }

    /// <summary>Maps a <c>type</c> value to a geometry, comparing bytes case-sensitively.</summary>
    /// <param name="value">The type value's raw content bytes, without the enclosing quotes.</param>
    /// <param name="typeName">The geometry the value names.</param>
    /// <returns><see langword="true"/> when the value is one of the geometry type names.</returns>
    private static bool TryClassifyGeometryType(ReadOnlySpan<byte> value, out GeometryTypeName typeName)
    {
        if(value.SequenceEqual("Point"u8))
        {
            typeName = GeometryTypeName.Point;

            return true;
        }

        if(value.SequenceEqual("MultiPoint"u8))
        {
            typeName = GeometryTypeName.MultiPoint;

            return true;
        }

        if(value.SequenceEqual("LineString"u8))
        {
            typeName = GeometryTypeName.LineString;

            return true;
        }

        if(value.SequenceEqual("MultiLineString"u8))
        {
            typeName = GeometryTypeName.MultiLineString;

            return true;
        }

        if(value.SequenceEqual("Polygon"u8))
        {
            typeName = GeometryTypeName.Polygon;

            return true;
        }

        if(value.SequenceEqual("MultiPolygon"u8))
        {
            typeName = GeometryTypeName.MultiPolygon;

            return true;
        }

        if(value.SequenceEqual("GeometryCollection"u8))
        {
            typeName = GeometryTypeName.GeometryCollection;

            return true;
        }

        typeName = GeometryTypeName.None;

        return false;
    }

    /// <summary>Whether the frame's container closes with a brace.</summary>
    /// <param name="kind">The frame's role.</param>
    /// <returns><see langword="true"/> for an object frame.</returns>
    private static bool IsObjectFrame(FrameKind kind)
    {
        return kind is FrameKind.GeometryObject
            or FrameKind.ForeignObject
            or FrameKind.CoordinateReferenceObject
            or FrameKind.CoordinateReferenceProperties;
    }

    /// <summary>Whether the byte is JSON whitespace: space, tab, carriage return, or line feed.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for a whitespace byte.</returns>
    private static bool IsWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    /// <summary>Advances the index past any whitespace.</summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, advanced past whitespace.</param>
    private static void SkipWhitespace(ReadOnlySpan<byte> body, ref int index)
    {
        while(index < body.Length && IsWhitespace(body[index]))
        {
            index++;
        }
    }

    /// <summary>Whether the byte is an ASCII digit.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for <c>0</c>-<c>9</c>.</returns>
    private static bool IsAsciiDigit(byte value)
    {
        return (uint)(value - (byte)'0') <= 9;
    }

    /// <summary>Whether the byte is a hexadecimal digit in either case.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for <c>0</c>-<c>9</c>, <c>a</c>-<c>f</c>, or <c>A</c>-<c>F</c>.</returns>
    private static bool IsHexDigit(byte value)
    {
        return IsAsciiDigit(value) || (uint)((value | 0x20) - (byte)'a') <= 'f' - 'a';
    }

    /// <summary>Whether the byte can begin a JSON number: a digit or the minus sign.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for a number-start byte.</returns>
    private static bool IsNumberStart(byte value)
    {
        return value == (byte)'-' || IsAsciiDigit(value);
    }

    /// <summary>Reads a fixed keyword when the position carries it.</summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, advanced past the keyword when it matches.</param>
    /// <param name="keyword">The keyword bytes.</param>
    /// <returns><see langword="true"/> when the keyword was read.</returns>
    private static bool TryReadKeyword(ReadOnlySpan<byte> body, ref int index, ReadOnlySpan<byte> keyword)
    {
        if(index + keyword.Length > body.Length || !body.Slice(index, keyword.Length).SequenceEqual(keyword))
        {
            return false;
        }

        index += keyword.Length;

        return true;
    }

    /// <summary>
    /// Reads one JSON number: an optional minus sign, an integer part that is either a single zero or a
    /// non-zero leading digit followed by digits, an optional fraction of one or more digits, and an
    /// optional exponent of one or more digits with an optional sign.
    /// </summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, advanced past the number when it is valid.</param>
    /// <returns><see langword="true"/> when a valid number was read.</returns>
    private static bool TryReadNumber(ReadOnlySpan<byte> body, ref int index)
    {
        int i = index;
        if(i < body.Length && body[i] == (byte)'-')
        {
            i++;
        }

        if(i == body.Length || !IsAsciiDigit(body[i]))
        {
            return false;
        }

        if(body[i] == (byte)'0')
        {
            i++;
        }
        else
        {
            while(i < body.Length && IsAsciiDigit(body[i]))
            {
                i++;
            }
        }

        if(i < body.Length && body[i] == (byte)'.')
        {
            i++;
            int fractionDigits = 0;
            while(i < body.Length && IsAsciiDigit(body[i]))
            {
                i++;
                fractionDigits++;
            }

            if(fractionDigits == 0)
            {
                return false;
            }
        }

        if(i < body.Length && (body[i] | 0x20) == (byte)'e')
        {
            i++;
            if(i < body.Length && body[i] is (byte)'+' or (byte)'-')
            {
                i++;
            }

            int exponentDigits = 0;
            while(i < body.Length && IsAsciiDigit(body[i]))
            {
                i++;
                exponentDigits++;
            }

            if(exponentDigits == 0)
            {
                return false;
            }
        }

        index = i;

        return true;
    }

    /// <summary>
    /// Reads one JSON string, validating every escape and rejecting an unescaped control byte below
    /// <c>0x20</c>. The reported content is the raw bytes between the quotes, escapes left as written.
    /// </summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, at the opening quote, advanced past the closing quote when the string is valid.</param>
    /// <param name="contentStart">The index of the string's first content byte.</param>
    /// <param name="contentLength">The length of the string's raw content.</param>
    /// <param name="hasEscape">Whether the content carries at least one backslash escape.</param>
    /// <returns><see langword="true"/> when a valid string was read.</returns>
    private static bool TryScanString(ReadOnlySpan<byte> body, ref int index, out int contentStart, out int contentLength, out bool hasEscape)
    {
        contentStart = index + 1;
        contentLength = 0;
        hasEscape = false;

        int i = contentStart;
        while(i < body.Length)
        {
            byte current = body[i];
            if(current == (byte)'"')
            {
                contentLength = i - contentStart;
                index = i + 1;

                return true;
            }

            if(current < 0x20)
            {
                return false;
            }

            if(current != (byte)'\\')
            {
                i++;
                continue;
            }

            hasEscape = true;
            i++;
            if(i == body.Length)
            {
                return false;
            }

            byte escape = body[i];
            if(escape is (byte)'"' or (byte)'\\' or (byte)'/' or (byte)'b' or (byte)'f' or (byte)'n' or (byte)'r' or (byte)'t')
            {
                i++;
                continue;
            }

            if(escape != (byte)'u')
            {
                return false;
            }

            i++;
            if(i + 4 > body.Length)
            {
                return false;
            }

            if(!IsHexDigit(body[i]) || !IsHexDigit(body[i + 1]) || !IsHexDigit(body[i + 2]) || !IsHexDigit(body[i + 3]))
            {
                return false;
            }

            i += 4;
        }

        return false;
    }
}
