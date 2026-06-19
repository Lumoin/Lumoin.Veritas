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
/// are encapsulated here.
/// </para>
/// </remarks>
public sealed class RdfTermJsonConverter: JsonConverter<RdfTerm>
{
    private const string TypeProperty = "type";
    private const string ValueProperty = "value";
    private const string DatatypeProperty = "datatype";
    private const string LanguageProperty = "language";
    private const string DirectionProperty = "direction";
    private const string SubjectProperty = "subject";
    private const string PredicateProperty = "predicate";
    private const string ObjectProperty = "object";

    private const string TypeUri = "uri";
    private const string TypeBnode = "bnode";
    private const string TypeLiteral = "literal";
    private const string TypeTriple = "triple";

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

            string propertyName = reader.GetString()!;
            if(!reader.Read())
            {
                throw new JsonException("Unexpected end of JSON after a property name.");
            }

            ReadFrame current = stack.Peek();
            switch(propertyName)
            {
                case(TypeProperty):
                {
                    current.Type = reader.GetString();
                    break;
                }
                case(ValueProperty):
                {
                    current.Value = reader.GetString();
                    break;
                }
                case(DatatypeProperty):
                {
                    current.Datatype = reader.GetString();
                    break;
                }
                case(LanguageProperty):
                {
                    current.Language = reader.GetString();
                    break;
                }
                case(DirectionProperty):
                {
                    current.Direction = reader.GetString();
                    break;
                }
                case(SubjectProperty):
                {
                    BeginChild(stack, current, ChildSlot.Subject, reader.TokenType);
                    break;
                }
                case(PredicateProperty):
                {
                    BeginChild(stack, current, ChildSlot.Predicate, reader.TokenType);
                    break;
                }
                case(ObjectProperty):
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

    /// <summary>Builds the RDF term for a completed frame from its accumulated type discriminator, scalars, and children.</summary>
    /// <param name="frame">The completed frame.</param>
    /// <returns>The built term.</returns>
    /// <exception cref="JsonException">The frame's <c>type</c> is unknown.</exception>
    private static RdfTerm BuildTerm(ReadFrame frame)
    {
        return frame.Type switch
        {
            TypeUri => new NamedNode(Utf8String.FromString(frame.Value!)),
            TypeBnode => new BlankNode(Utf8String.FromString(frame.Value!)),
            TypeLiteral => CreateLiteral(frame.Value!, frame.Datatype, frame.Language, frame.Direction),
            TypeTriple => new TripleTerm(frame.Subject!, (NamedNode)frame.Predicate!, frame.Object!),
            _ => throw new JsonException($"Unknown RDF term type: '{frame.Type}'.")
        };
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
                        work.Push(new TermStep(JsonStepKind.EndObject, null));
                        work.Push(new TermStep(JsonStepKind.Term, triple.Object));
                        work.Push(new TermStep(JsonStepKind.ObjectName, null));
                        work.Push(new TermStep(JsonStepKind.Term, triple.Predicate));
                        work.Push(new TermStep(JsonStepKind.PredicateName, null));
                        work.Push(new TermStep(JsonStepKind.Term, triple.Subject));
                        work.Push(new TermStep(JsonStepKind.SubjectName, null));
                        work.Push(new TermStep(JsonStepKind.StartTripleObject, null));
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
                writer.WriteString(ValueProperty, iri.ToString());
                break;
            }
            case(BlankNode(var label)):
            {
                writer.WriteString(TypeProperty, TypeBnode);
                writer.WriteString(ValueProperty, label.ToString());
                break;
            }
            case(Literal(var lexical, var dt, var lang, var dir)):
            {
                writer.WriteString(TypeProperty, TypeLiteral);
                writer.WriteString(ValueProperty, lexical.ToString());
                writer.WriteString(DatatypeProperty, dt.Iri.ToString());
                if(lang is { } l)
                {
                    writer.WriteString(LanguageProperty, l.ToString());
                }

                if(dir is { } d)
                {
                    writer.WriteString(DirectionProperty, TextDirections.ToText(d));
                }

                break;
            }
            default:
            {
                //An unknown term kind writes a bare object, preserving the original converter's behaviour.
                break;
            }
        }

        writer.WriteEndObject();
    }

    private static Literal CreateLiteral(string value, string? datatype, string? language, string? direction)
    {
        TextDirection? dir = TextDirections.TryParse(direction, out TextDirection parsed) ? parsed : null;

        Utf8String lexical = Utf8String.FromString(value);
        NamedNode dt = new(Utf8String.FromString(datatype ?? "http://www.w3.org/2001/XMLSchema#string"));

        Utf8String? lang = language is not null
            ? Utf8String.FromString(language)
            : null;

        return new Literal(lexical, dt, lang, dir);
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
    private readonly record struct TermStep(JsonStepKind Kind, RdfTerm? Term);

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
        public string? Type { get; set; }

        /// <summary>The <c>value</c> lexical form (for a leaf term).</summary>
        public string? Value { get; set; }

        /// <summary>The <c>datatype</c> IRI (for a literal).</summary>
        public string? Datatype { get; set; }

        /// <summary>The <c>language</c> tag (for a literal).</summary>
        public string? Language { get; set; }

        /// <summary>The <c>direction</c> token (for a literal).</summary>
        public string? Direction { get; set; }

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
