using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;

namespace Lumoin.Veritas.Rdf.Json;

/// <summary>
/// Converts <see cref="RdfTerm"/> and its subtypes to and from JSON.
/// </summary>
/// <remarks>
/// <para>
/// Serializes the RDF term hierarchy using a type discriminator following the
/// conventions of the SPARQL JSON results format: <c>"type"</c> is one of
/// <c>"uri"</c>, <c>"bnode"</c>, <c>"literal"</c>, or <c>"triple"</c>.
/// </para>
/// <para>
/// This converter handles the polymorphic dispatch that <see cref="RdfTerm"/>
/// requires. Domain types remain clean POCOs; all JSON structure decisions
/// are encapsulated here. The read path is byte-native: property names and the
/// type discriminator dispatch through <see cref="Utf8JsonReader.ValueTextEquals(System.ReadOnlySpan{byte})"/>
/// and every scalar value is copied straight from the reader's UTF-8 into an
/// owned <see cref="Utf8String"/>, so no <see cref="string"/> is materialised.
/// </para>
/// </remarks>
public sealed class RdfTermJsonConverter: JsonConverter<RdfTerm>
{
    /// <summary>The <c>type</c> discriminator property name.</summary>
    private static ReadOnlySpan<byte> TypeProperty => "type"u8;

    /// <summary>The <c>value</c> lexical property name.</summary>
    private static ReadOnlySpan<byte> ValueProperty => "value"u8;

    /// <summary>The <c>datatype</c> IRI property name.</summary>
    private static ReadOnlySpan<byte> DatatypeProperty => "datatype"u8;

    /// <summary>The <c>language</c> tag property name.</summary>
    private static ReadOnlySpan<byte> LanguageProperty => "language"u8;

    /// <summary>The <c>direction</c> base-direction property name.</summary>
    private static ReadOnlySpan<byte> DirectionProperty => "direction"u8;

    /// <summary>The <c>subject</c> triple-component property name.</summary>
    private static ReadOnlySpan<byte> SubjectProperty => "subject"u8;

    /// <summary>The <c>predicate</c> triple-component property name.</summary>
    private static ReadOnlySpan<byte> PredicateProperty => "predicate"u8;

    /// <summary>The <c>object</c> triple-component property name.</summary>
    private static ReadOnlySpan<byte> ObjectProperty => "object"u8;

    /// <summary>The <c>uri</c> type discriminator value.</summary>
    private static ReadOnlySpan<byte> TypeUri => "uri"u8;

    /// <summary>The <c>bnode</c> type discriminator value.</summary>
    private static ReadOnlySpan<byte> TypeBnode => "bnode"u8;

    /// <summary>The <c>literal</c> type discriminator value.</summary>
    private static ReadOnlySpan<byte> TypeLiteral => "literal"u8;

    /// <summary>The <c>triple</c> type discriminator value.</summary>
    private static ReadOnlySpan<byte> TypeTriple => "triple"u8;

    /// <summary>The default <c>xsd:string</c> datatype of a literal with no declared datatype.</summary>
    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    /// <inheritdoc/>
    /// <remarks>
    /// A quoted triple nests other term objects in its subject/predicate/object slots. The parse drives a single
    /// <see cref="Utf8JsonReader"/> (a ref struct that cannot be stored) over an explicit stack of frames — one per
    /// open term object — building each term at its closing brace, so the parse never recurses. A frame's scalar
    /// properties and built children are accumulated regardless of order; a triple is materialised once its closing
    /// brace is reached. Nesting depth is bounded by the reader's own <see cref="JsonReaderOptions.MaxDepth"/> (deep
    /// input throws <see cref="JsonException"/>), so no separate quoted-triple cap is enforced here.
    /// </remarks>
    public override RdfTerm? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        ArgumentNullException.ThrowIfNull(options);

        if(reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for RdfTerm.");
        }

        Stack<ReadFrame> stack = new();
        stack.Push(new ReadFrame());
        RdfTerm? result = null;

        while(stack.Count > 0)
        {
            if(!reader.Read())
            {
                throw new JsonException("Unexpected end of JSON while reading an RdfTerm.");
            }

            if(reader.TokenType == JsonTokenType.EndObject)
            {
                //The current term object is complete; build it and hand it to its parent (or return it as the root).
                RdfTerm term = BuildTerm(stack.Pop());
                if(stack.Count == 0)
                {
                    result = term;
                }
                else
                {
                    Assign(stack.Peek(), term);
                }

                continue;
            }

            if(reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name.");
            }

            PropertyKind property = ClassifyProperty(ref reader);
            if(!reader.Read())
            {
                throw new JsonException("Unexpected end of JSON after a property name.");
            }

            ReadFrame current = stack.Peek();
            switch(property)
            {
                case(PropertyKind.Type):
                {
                    current.Type = ClassifyType(ref reader);
                    break;
                }
                case(PropertyKind.Value):
                {
                    current.Value = ReadUtf8(ref reader);
                    break;
                }
                case(PropertyKind.Datatype):
                {
                    current.Datatype = ReadUtf8(ref reader);
                    break;
                }
                case(PropertyKind.Language):
                {
                    current.Language = ReadUtf8(ref reader);
                    break;
                }
                case(PropertyKind.Direction):
                {
                    current.Direction = ReadUtf8(ref reader);
                    break;
                }
                case(PropertyKind.Subject):
                {
                    BeginChild(stack, current, ChildSlot.Subject, reader.TokenType);
                    break;
                }
                case(PropertyKind.Predicate):
                {
                    BeginChild(stack, current, ChildSlot.Predicate, reader.TokenType);
                    break;
                }
                case(PropertyKind.Object):
                {
                    BeginChild(stack, current, ChildSlot.Object, reader.TokenType);
                    break;
                }
                default:
                {
                    reader.Skip();
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>Classifies the current property-name token against the known names without materialising a string.</summary>
    /// <param name="reader">The reader positioned on a property-name token.</param>
    /// <returns>The matched property, or <see cref="PropertyKind.Unknown"/>.</returns>
    private static PropertyKind ClassifyProperty(ref Utf8JsonReader reader)
    {
        if(reader.ValueTextEquals(TypeProperty))
        {
            return PropertyKind.Type;
        }

        if(reader.ValueTextEquals(ValueProperty))
        {
            return PropertyKind.Value;
        }

        if(reader.ValueTextEquals(DatatypeProperty))
        {
            return PropertyKind.Datatype;
        }

        if(reader.ValueTextEquals(LanguageProperty))
        {
            return PropertyKind.Language;
        }

        if(reader.ValueTextEquals(DirectionProperty))
        {
            return PropertyKind.Direction;
        }

        if(reader.ValueTextEquals(SubjectProperty))
        {
            return PropertyKind.Subject;
        }

        if(reader.ValueTextEquals(PredicateProperty))
        {
            return PropertyKind.Predicate;
        }

        if(reader.ValueTextEquals(ObjectProperty))
        {
            return PropertyKind.Object;
        }

        return PropertyKind.Unknown;
    }

    /// <summary>Classifies the current string token as a type discriminator without materialising a string.</summary>
    /// <param name="reader">The reader positioned on the type value token.</param>
    /// <returns>The matched term kind, or <see cref="TermKind.Unknown"/>.</returns>
    private static TermKind ClassifyType(ref Utf8JsonReader reader)
    {
        if(reader.ValueTextEquals(TypeUri))
        {
            return TermKind.Uri;
        }

        if(reader.ValueTextEquals(TypeBnode))
        {
            return TermKind.Bnode;
        }

        if(reader.ValueTextEquals(TypeLiteral))
        {
            return TermKind.Literal;
        }

        if(reader.ValueTextEquals(TypeTriple))
        {
            return TermKind.Triple;
        }

        return TermKind.Unknown;
    }

    /// <summary>Copies the current string token's unescaped UTF-8 into an owned <see cref="Utf8String"/>.</summary>
    /// <param name="reader">The reader positioned on a string value token.</param>
    /// <returns>The decoded value.</returns>
    private static Utf8String ReadUtf8(ref Utf8JsonReader reader)
    {
        int maxLength = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        byte[] buffer = new byte[maxLength];
        int written = reader.CopyString(buffer);

        return new Utf8String(new ReadOnlyMemory<byte>(buffer, 0, written));
    }

    /// <summary>Builds the RDF term for a completed frame from its accumulated type discriminator, scalars, and children.</summary>
    /// <param name="frame">The completed frame.</param>
    /// <returns>The built term.</returns>
    /// <exception cref="JsonException">The frame's <c>type</c> is unknown.</exception>
    private static RdfTerm BuildTerm(ReadFrame frame)
    {
        return frame.Type switch
        {
            TermKind.Uri => new NamedNode(frame.Value ?? default),
            TermKind.Bnode => new BlankNode(frame.Value ?? default),
            TermKind.Literal => CreateLiteral(frame),
            TermKind.Triple => new TripleTerm(frame.Subject!, (NamedNode)frame.Predicate!, frame.Object!),
            _ => throw new JsonException("Unknown RDF term type.")
        };
    }

    /// <summary>Builds a literal from a completed frame, defaulting an absent datatype to <c>xsd:string</c> and parsing the base direction.</summary>
    /// <param name="frame">The completed literal frame.</param>
    /// <returns>The literal term.</returns>
    private static Literal CreateLiteral(ReadFrame frame)
    {
        NamedNode datatype = frame.Datatype is Utf8String dt ? new NamedNode(dt) : XsdString;
        TextDirection? direction = frame.Direction is Utf8String dir && TextDirections.TryParse(dir.Span, out TextDirection parsed) ? parsed : null;

        return new Literal(frame.Value ?? default, datatype, frame.Language, direction);
    }

    /// <summary>Stores a built child term into its parent frame's pending triple-component slot.</summary>
    /// <param name="parent">The parent frame.</param>
    /// <param name="child">The built child term.</param>
    private static void Assign(ReadFrame parent, RdfTerm child)
    {
        switch(parent.Pending)
        {
            case(ChildSlot.Subject):
            {
                parent.Subject = child;
                break;
            }
            case(ChildSlot.Predicate):
            {
                parent.Predicate = child;
                break;
            }
            default:
            {
                parent.Object = child;
                break;
            }
        }
    }

    /// <summary>Marks the parent's pending component slot and pushes a fresh child frame for a nested term object.</summary>
    /// <param name="stack">The frame stack.</param>
    /// <param name="parent">The parent frame whose component is being read.</param>
    /// <param name="slot">The component slot the nested term fills.</param>
    /// <param name="valueToken">The token type of the component's value, which must open an object.</param>
    /// <exception cref="JsonException">The component's value is not an object.</exception>
    private static void BeginChild(Stack<ReadFrame> stack, ReadFrame parent, ChildSlot slot, JsonTokenType valueToken)
    {
        if(valueToken != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object for a triple term component.");
        }

        parent.Pending = slot;
        stack.Push(new ReadFrame());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A quoted triple is walked over an explicit work-stack (no recursion); a leaf is written directly on the
    /// fast path. Nesting beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/> raises
    /// <see cref="TripleTermDepthLimitException"/> rather than overflowing the call stack or the JSON writer.
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, RdfTerm value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        if(value is not TripleTerm root)
        {
            WriteLeaf(writer, value);

            return;
        }

        Stack<TermStep> work = new();
        work.Push(new TermStep(JsonStepKind.Term, root));
        int depth = 0;

        while(work.Count > 0)
        {
            TermStep step = work.Pop();
            switch(step.Kind)
            {
                case(JsonStepKind.Term):
                {
                    if(step.Term is TripleTerm triple)
                    {
                        depth++;
                        if(depth > QuotedTripleLimits.MaxNestingDepth)
                        {
                            throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                        }

                        //Push in reverse so they pop in emission order: { type, subject, predicate, object }.
                        work.Push(TermStep.EndObject);
                        work.Push(new TermStep(JsonStepKind.Term, triple.Object));
                        work.Push(TermStep.ObjectName);
                        work.Push(new TermStep(JsonStepKind.Term, triple.Predicate));
                        work.Push(TermStep.PredicateName);
                        work.Push(new TermStep(JsonStepKind.Term, triple.Subject));
                        work.Push(TermStep.SubjectName);
                        work.Push(TermStep.StartTripleObject);
                    }
                    else
                    {
                        WriteLeaf(writer, step.Term!);
                    }

                    break;
                }
                case(JsonStepKind.StartTripleObject):
                {
                    writer.WriteStartObject();
                    writer.WriteString(TypeProperty, TypeTriple);
                    break;
                }
                case(JsonStepKind.SubjectName):
                {
                    writer.WritePropertyName(SubjectProperty);
                    break;
                }
                case(JsonStepKind.PredicateName):
                {
                    writer.WritePropertyName(PredicateProperty);
                    break;
                }
                case(JsonStepKind.ObjectName):
                {
                    writer.WritePropertyName(ObjectProperty);
                    break;
                }
                default:
                {
                    //JsonStepKind.EndObject: close the triple object and unwind one nesting level.
                    writer.WriteEndObject();
                    depth--;
                    break;
                }
            }
        }
    }

    /// <summary>Writes a leaf term (named node, blank node, or literal) as its complete JSON object.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="term">The leaf term.</param>
    private static void WriteLeaf(Utf8JsonWriter writer, RdfTerm term)
    {
        writer.WriteStartObject();

        switch(term)
        {
            case(NamedNode(var iri)):
            {
                writer.WriteString(TypeProperty, TypeUri);
                writer.WriteString(ValueProperty, iri.Span);
                break;
            }
            case(BlankNode(var label)):
            {
                writer.WriteString(TypeProperty, TypeBnode);
                writer.WriteString(ValueProperty, label.Span);
                break;
            }
            case(Literal(var lexical, var dt, var lang, var dir)):
            {
                writer.WriteString(TypeProperty, TypeLiteral);
                writer.WriteString(ValueProperty, lexical.Span);
                writer.WriteString(DatatypeProperty, dt.Iri.Span);
                if(lang is { } l)
                {
                    writer.WriteString(LanguageProperty, l.Span);
                }

                if(dir is { } d)
                {
                    writer.WriteString(DirectionProperty, TextDirections.ToText(d));
                }

                break;
            }
            case(EngineNode):
            {
                //An engine-minted node never round-trips through a converter: reconstructing it from JSON
                //would let input bytes forge a term equal to an engine mint, so the write refuses loudly.
                throw new NotSupportedException("An engine-minted node cannot be serialized as a JSON RDF term.");
            }
            default:
            {
                //An unknown term kind writes a bare object, preserving the original converter's behaviour.
                break;
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>The kind of step on the quoted-triple serialization work-stack.</summary>
    private enum JsonStepKind
    {
        /// <summary>Write a term: a leaf object directly, or expand a quoted triple into its component steps.</summary>
        Term,

        /// <summary>Open a quoted triple's object and write its <c>type</c> discriminator.</summary>
        StartTripleObject,

        /// <summary>Write the <c>subject</c> property name.</summary>
        SubjectName,

        /// <summary>Write the <c>predicate</c> property name.</summary>
        PredicateName,

        /// <summary>Write the <c>object</c> property name.</summary>
        ObjectName,

        /// <summary>Close a quoted triple's object and unwind one nesting level.</summary>
        EndObject
    }

    /// <summary>One step on the quoted-triple serialization work-stack.</summary>
    /// <param name="Kind">The step kind.</param>
    /// <param name="Term">The term to write; set only for a <c>Term</c> step.</param>
    private readonly record struct TermStep(JsonStepKind Kind, RdfTerm? Term)
    {
        /// <summary>The payload-less step that opens a quoted triple's object and writes its <c>type</c> discriminator.</summary>
        public static TermStep StartTripleObject { get; } = new(JsonStepKind.StartTripleObject, null);

        /// <summary>The payload-less step that writes the <c>subject</c> property name.</summary>
        public static TermStep SubjectName { get; } = new(JsonStepKind.SubjectName, null);

        /// <summary>The payload-less step that writes the <c>predicate</c> property name.</summary>
        public static TermStep PredicateName { get; } = new(JsonStepKind.PredicateName, null);

        /// <summary>The payload-less step that writes the <c>object</c> property name.</summary>
        public static TermStep ObjectName { get; } = new(JsonStepKind.ObjectName, null);

        /// <summary>The payload-less step that closes a quoted triple's object and unwinds one nesting level.</summary>
        public static TermStep EndObject { get; } = new(JsonStepKind.EndObject, null);
    }

    /// <summary>A binding value object's property, classified from its UTF-8 name.</summary>
    private enum PropertyKind
    {
        /// <summary>An unrecognised property, skipped.</summary>
        Unknown,

        /// <summary>The <c>type</c> discriminator.</summary>
        Type,

        /// <summary>The <c>value</c> lexical form.</summary>
        Value,

        /// <summary>The <c>datatype</c> IRI.</summary>
        Datatype,

        /// <summary>The <c>language</c> tag.</summary>
        Language,

        /// <summary>The <c>direction</c> base direction.</summary>
        Direction,

        /// <summary>The <c>subject</c> triple component.</summary>
        Subject,

        /// <summary>The <c>predicate</c> triple component.</summary>
        Predicate,

        /// <summary>The <c>object</c> triple component.</summary>
        Object
    }

    /// <summary>An RDF term's kind, classified from its <c>type</c> discriminator.</summary>
    private enum TermKind
    {
        /// <summary>An unrecognised or absent type.</summary>
        Unknown,

        /// <summary>An IRI (<c>uri</c>).</summary>
        Uri,

        /// <summary>A blank node (<c>bnode</c>).</summary>
        Bnode,

        /// <summary>A literal.</summary>
        Literal,

        /// <summary>A quoted triple (<c>triple</c>).</summary>
        Triple
    }

    /// <summary>The triple component a nested child term fills in its parent frame.</summary>
    private enum ChildSlot
    {
        /// <summary>The child fills no component (a leaf frame).</summary>
        None,

        /// <summary>The child is the parent triple's subject.</summary>
        Subject,

        /// <summary>The child is the parent triple's predicate.</summary>
        Predicate,

        /// <summary>The child is the parent triple's object.</summary>
        Object
    }

    /// <summary>The mutable parse state for one open term object on the deserialization stack.</summary>
    private sealed class ReadFrame
    {
        /// <summary>The <c>type</c> discriminator.</summary>
        public TermKind Type { get; set; }

        /// <summary>The <c>value</c> lexical form (for a leaf term).</summary>
        public Utf8String? Value { get; set; }

        /// <summary>The <c>datatype</c> IRI (for a literal).</summary>
        public Utf8String? Datatype { get; set; }

        /// <summary>The <c>language</c> tag (for a literal).</summary>
        public Utf8String? Language { get; set; }

        /// <summary>The <c>direction</c> token (for a literal).</summary>
        public Utf8String? Direction { get; set; }

        /// <summary>The built subject term (for a triple).</summary>
        public RdfTerm? Subject { get; set; }

        /// <summary>The built predicate term (for a triple).</summary>
        public RdfTerm? Predicate { get; set; }

        /// <summary>The built object term (for a triple).</summary>
        public RdfTerm? Object { get; set; }

        /// <summary>The component slot the next completed child term fills.</summary>
        public ChildSlot Pending { get; set; }
    }
}
