using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.Rdf.Json;

/// <summary>
/// Reads the <see href="https://www.w3.org/TR/sparql11-results-json/">SPARQL 1.1 Query Results JSON Format</see>
/// (the <c>.srj</c> serialization) into a <see cref="SparqlResultSet"/>: a <c>SELECT</c> result's <c>head.vars</c>
/// and <c>results.bindings</c>, or an <c>ASK</c> result's <c>boolean</c>. RDF 1.2 triple-term binding values
/// (<c>"type":"triple"</c>) are supported.
/// </summary>
/// <remarks>
/// <para>
/// The reader is byte-native: it drives a single <see cref="Utf8JsonReader"/> over the UTF-8 input, dispatches
/// property names and the type discriminator through <see cref="Utf8JsonReader.ValueTextEquals(System.ReadOnlySpan{byte})"/>,
/// and copies each scalar straight from the reader into an owned <see cref="Utf8String"/>, so no <see cref="string"/>
/// is materialised on the parse path and the result set is independent of the input buffer.
/// </para>
/// <para>
/// Binding value objects carry a <c>type</c> (<c>uri</c>, <c>literal</c>, the legacy <c>typed-literal</c>,
/// <c>bnode</c>, or <c>triple</c>) and a <c>value</c>. A <c>literal</c> is typed <c>rdf:langString</c> when it
/// carries <c>xml:lang</c> (<c>rdf:dirLangString</c> when it also carries <c>its:dir</c>), its declared
/// <c>datatype</c>, or <c>xsd:string</c> otherwise. A <c>triple</c>'s <c>value</c> is an object with nested
/// <c>subject</c>/<c>predicate</c>/<c>object</c> term objects; it parses over an explicit stack, so deep nesting
/// cannot overflow the call stack.
/// </para>
/// </remarks>
public static class SparqlResultsJsonReader
{
    /// <summary>The <c>xsd:string</c> datatype of a plain literal.</summary>
    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    /// <summary>The <c>rdf:langString</c> datatype of a language-tagged literal.</summary>
    private static NamedNode RdfLangString { get; } = new(Vocabulary.Rdf.LangString);

    /// <summary>The <c>rdf:dirLangString</c> datatype of a directional language-tagged literal.</summary>
    private static NamedNode RdfDirLangString { get; } = new(Vocabulary.Rdf.DirLangString);

    /// <summary>Reads a result set from the in-memory JSON bytes.</summary>
    /// <param name="bytes">The <c>.srj</c> document bytes.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results JSON.</exception>
    public static SparqlResultSet Read(ReadOnlyMemory<byte> bytes)
    {
        Utf8JsonReader reader = new(bytes.Span);

        return ReadDocument(ref reader);
    }

    /// <summary>Reads a result set from a possibly-segmented in-memory JSON byte sequence, such as a pipe's buffer, with no contiguous copy.</summary>
    /// <param name="bytes">The <c>.srj</c> document bytes, possibly spanning segments.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results JSON.</exception>
    public static SparqlResultSet Read(ReadOnlySequence<byte> bytes)
    {
        Utf8JsonReader reader = new(bytes);

        return ReadDocument(ref reader);
    }

    /// <summary>Reads a result set from a JSON stream.</summary>
    /// <param name="stream">The stream over the <c>.srj</c> document.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results JSON.</exception>
    public static SparqlResultSet Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);

        return Read(new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length));
    }

    /// <summary>Builds the result set from the JSON token stream, wrapping a malformed-JSON error as a format error.</summary>
    /// <param name="reader">The reader positioned before the root token.</param>
    /// <returns>The result set.</returns>
    /// <exception cref="FormatException">The document is malformed or does not have the SPARQL-results shape.</exception>
    private static SparqlResultSet ReadDocument(ref Utf8JsonReader reader)
    {
        try
        {
            if(!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new FormatException("SPARQL Results JSON must be a top-level object.");
            }

            List<Utf8String> variables = [];
            List<SparqlSolution> solutions = [];
            bool isAsk = false;
            bool askValue = false;
            bool sawBindings = false;

            while(reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if(reader.ValueTextEquals(SparqlResultsJsonNames.BooleanUtf8))
                {
                    reader.Read();
                    askValue = ReadBoolean(ref reader);
                    isAsk = true;
                }
                else if(reader.ValueTextEquals(SparqlResultsJsonNames.HeadUtf8))
                {
                    reader.Read();
                    ReadHead(ref reader, variables);
                }
                else if(reader.ValueTextEquals(SparqlResultsJsonNames.ResultsUtf8))
                {
                    reader.Read();
                    sawBindings |= ReadResults(ref reader, solutions);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if(isAsk)
            {
                return SparqlResultSet.ForAsk(askValue);
            }

            if(!sawBindings)
            {
                throw new FormatException("SPARQL SELECT results JSON is missing its results.bindings array.");
            }

            return SparqlResultSet.ForSelect(variables, solutions);
        }
        catch(JsonException ex)
        {
            throw new FormatException($"Malformed SPARQL Results JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Reads the <c>boolean</c> of an <c>ASK</c> result, accepting a JSON boolean or the strings <c>true</c>/<c>false</c>.</summary>
    /// <param name="reader">The reader positioned on the boolean value token.</param>
    /// <returns>The boolean answer.</returns>
    /// <exception cref="FormatException">The value is neither a JSON boolean nor <c>true</c>/<c>false</c>.</exception>
    private static bool ReadBoolean(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when reader.ValueTextEquals("true"u8) => true,
            JsonTokenType.String when reader.ValueTextEquals("false"u8) => false,
            _ => throw new FormatException("A SPARQL ASK result's 'boolean' must be true or false.")
        };
    }

    /// <summary>Reads the head's declared variables (<c>head.vars</c>) in document order; tolerant of an absent or non-array <c>vars</c>.</summary>
    /// <param name="reader">The reader positioned on the head value.</param>
    /// <param name="variables">The list the variable names are appended to.</param>
    private static void ReadHead(ref Utf8JsonReader reader, List<Utf8String> variables)
    {
        if(reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();

            return;
        }

        while(reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if(reader.ValueTextEquals(SparqlResultsJsonNames.VarsUtf8))
            {
                reader.Read();
                if(reader.TokenType == JsonTokenType.StartArray)
                {
                    while(reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if(reader.TokenType == JsonTokenType.String)
                        {
                            variables.Add(ReadUtf8(ref reader));
                        }
                    }
                }
                else
                {
                    reader.Skip();
                }
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
    }

    /// <summary>Reads the <c>results.bindings</c> array into solutions.</summary>
    /// <param name="reader">The reader positioned on the results value.</param>
    /// <param name="solutions">The list the solutions are appended to.</param>
    /// <returns><see langword="true"/> when a <c>bindings</c> array was present.</returns>
    /// <exception cref="FormatException">A <c>bindings</c> property is present but is not an array of objects.</exception>
    private static bool ReadResults(ref Utf8JsonReader reader, List<SparqlSolution> solutions)
    {
        if(reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();

            return false;
        }

        bool sawBindings = false;
        while(reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if(reader.ValueTextEquals(SparqlResultsJsonNames.BindingsUtf8))
            {
                reader.Read();
                if(reader.TokenType != JsonTokenType.StartArray)
                {
                    throw new FormatException("SPARQL SELECT results JSON is missing its results.bindings array.");
                }

                while(reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if(reader.TokenType != JsonTokenType.StartObject)
                    {
                        throw new FormatException("A SPARQL Results JSON binding set must be an object.");
                    }

                    solutions.Add(ReadSolution(ref reader));
                }

                sawBindings = true;
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        return sawBindings;
    }

    /// <summary>Reads one binding-set object into a solution, binding each named property to its parsed term.</summary>
    /// <param name="reader">The reader positioned on the binding-set object's start.</param>
    /// <returns>The solution mapping.</returns>
    /// <exception cref="FormatException">A binding value is not an object.</exception>
    private static SparqlSolution ReadSolution(ref Utf8JsonReader reader)
    {
        List<SparqlBinding> bindings = [];
        while(reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            Utf8String variable = ReadUtf8(ref reader);
            reader.Read();
            if(reader.TokenType != JsonTokenType.StartObject)
            {
                throw new FormatException("A SPARQL Results JSON binding value must be an object.");
            }

            bindings.Add(new SparqlBinding(new SparqlVariable(variable), ReadTerm(ref reader)));
        }

        return new SparqlSolution(bindings);
    }

    /// <summary>
    /// Parses a binding value object to an RDF term over an explicit stack, so a deeply-nested triple term cannot
    /// overflow the call stack. A frame's <see cref="TermFrame.InValue"/> flag distinguishes a leaf's string
    /// <c>value</c> from a triple's <c>value</c> wrapper object, whose <c>subject</c>/<c>predicate</c>/<c>object</c>
    /// properties push child term frames. Properties are accepted in any order; a term is built at its closing brace.
    /// </summary>
    /// <param name="reader">The reader positioned on the term object's start.</param>
    /// <returns>The parsed term.</returns>
    /// <exception cref="FormatException">The value object is malformed.</exception>
    private static RdfTerm ReadTerm(ref Utf8JsonReader reader)
    {
        Stack<TermFrame> stack = new();
        stack.Push(new TermFrame());
        RdfTerm? result = null;

        while(stack.Count > 0)
        {
            if(!reader.Read())
            {
                throw new FormatException("Unexpected end of JSON while reading a binding value.");
            }

            TermFrame frame = stack.Peek();
            if(reader.TokenType == JsonTokenType.EndObject)
            {
                if(frame.InValue)
                {
                    //The triple's value wrapper closed; return to the triple frame's top-level properties.
                    frame.InValue = false;
                }
                else
                {
                    RdfTerm term = BuildTerm(stack.Pop());
                    if(stack.Count == 0)
                    {
                        result = term;
                    }
                    else
                    {
                        Assign(stack.Peek(), term);
                    }
                }

                continue;
            }

            if(reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new FormatException("Expected a property name in a SPARQL Results JSON binding value.");
            }

            if(frame.InValue)
            {
                ComponentSlot slot = ClassifyComponent(ref reader);
                reader.Read();
                if(reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new FormatException("A SPARQL Results JSON triple component must be an object.");
                }

                frame.Pending = slot;
                stack.Push(new TermFrame());

                continue;
            }

            PropertyKind property = ClassifyProperty(ref reader);
            reader.Read();
            switch(property)
            {
                case(PropertyKind.Type):
                {
                    frame.Kind = ClassifyType(ref reader);
                    break;
                }
                case(PropertyKind.Value):
                {
                    if(reader.TokenType == JsonTokenType.StartObject)
                    {
                        //A triple's value is the wrapper object holding its components.
                        frame.InValue = true;
                    }
                    else
                    {
                        frame.Lexical = ReadUtf8(ref reader);
                    }

                    break;
                }
                case(PropertyKind.Datatype):
                {
                    frame.Datatype = ReadUtf8(ref reader);
                    break;
                }
                case(PropertyKind.Language):
                {
                    frame.Language = ReadUtf8(ref reader);
                    break;
                }
                case(PropertyKind.Direction):
                {
                    frame.Direction = ReadUtf8(ref reader);
                    break;
                }
                default:
                {
                    reader.Skip();
                    break;
                }
            }
        }

        return result ?? throw new FormatException("A SPARQL Results JSON binding value did not produce a term.");
    }

    /// <summary>Builds the term for a completed frame from its type discriminator, scalars, and components.</summary>
    /// <param name="frame">The completed frame.</param>
    /// <returns>The built term.</returns>
    /// <exception cref="FormatException">The frame's type is unsupported, or a triple component is missing or ill-typed.</exception>
    private static RdfTerm BuildTerm(TermFrame frame)
    {
        return frame.Kind switch
        {
            TermKind.Uri => new NamedNode(frame.Lexical ?? default),
            TermKind.Bnode => new BlankNode(frame.Lexical ?? default),
            TermKind.Literal => BuildLiteral(frame),
            TermKind.Triple => BuildTriple(frame),
            _ => throw new FormatException("Unsupported SPARQL Results JSON binding value type.")
        };
    }

    /// <summary>Builds a literal: <c>rdf:dirLangString</c> with both <c>xml:lang</c> and <c>its:dir</c>, <c>rdf:langString</c> for <c>xml:lang</c> alone, its declared datatype, or <c>xsd:string</c>.</summary>
    /// <param name="frame">The completed literal frame.</param>
    /// <returns>The literal term.</returns>
    private static Literal BuildLiteral(TermFrame frame)
    {
        Utf8String lexical = frame.Lexical ?? default;
        if(frame.Language is Utf8String tag)
        {
            if(frame.Direction is Utf8String dir && TextDirections.TryParse(dir.Span, out TextDirection direction))
            {
                return new Literal(lexical, RdfDirLangString, tag, direction);
            }

            return new Literal(lexical, RdfLangString, tag);
        }

        if(frame.Datatype is Utf8String datatype)
        {
            return new Literal(lexical, new NamedNode(datatype));
        }

        return new Literal(lexical, XsdString);
    }

    /// <summary>Builds a triple term from its three parsed components.</summary>
    /// <param name="frame">The completed triple frame.</param>
    /// <returns>The triple term.</returns>
    /// <exception cref="FormatException">A component is missing, or the predicate is not an IRI.</exception>
    private static TripleTerm BuildTriple(TermFrame frame)
    {
        if(frame.Subject is null || frame.Predicate is null || frame.Object is null)
        {
            throw new FormatException("A SPARQL Results JSON triple value is missing a component.");
        }

        if(frame.Predicate is not NamedNode predicate)
        {
            throw new FormatException("A SPARQL Results JSON triple value has a non-IRI predicate term.");
        }

        return new TripleTerm(frame.Subject, predicate, frame.Object);
    }

    /// <summary>Stores a built component term into its parent triple frame's pending slot.</summary>
    /// <param name="parent">The parent triple frame.</param>
    /// <param name="child">The built component term.</param>
    private static void Assign(TermFrame parent, RdfTerm child)
    {
        switch(parent.Pending)
        {
            case(ComponentSlot.Subject):
            {
                parent.Subject = child;
                break;
            }
            case(ComponentSlot.Predicate):
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

    /// <summary>Classifies the current property-name token of a term object without materialising a string.</summary>
    /// <param name="reader">The reader positioned on a property-name token.</param>
    /// <returns>The matched property, or <see cref="PropertyKind.Unknown"/>.</returns>
    private static PropertyKind ClassifyProperty(ref Utf8JsonReader reader)
    {
        if(reader.ValueTextEquals(SparqlResultsJsonNames.TypeUtf8))
        {
            return PropertyKind.Type;
        }

        if(reader.ValueTextEquals(SparqlResultsJsonNames.ValueUtf8))
        {
            return PropertyKind.Value;
        }

        if(reader.ValueTextEquals(SparqlResultsJsonNames.DatatypeUtf8))
        {
            return PropertyKind.Datatype;
        }

        if(reader.ValueTextEquals(SparqlResultsJsonNames.LanguageUtf8))
        {
            return PropertyKind.Language;
        }

        if(reader.ValueTextEquals(SparqlResultsJsonNames.DirectionUtf8))
        {
            return PropertyKind.Direction;
        }

        return PropertyKind.Unknown;
    }

    /// <summary>Classifies the current type-discriminator string token without materialising a string.</summary>
    /// <param name="reader">The reader positioned on the type value token.</param>
    /// <returns>The matched term kind, or <see cref="TermKind.Unknown"/>.</returns>
    private static TermKind ClassifyType(ref Utf8JsonReader reader)
    {
        if(reader.ValueTextEquals(SparqlResultsJsonNames.UriUtf8))
        {
            return TermKind.Uri;
        }

        if(reader.ValueTextEquals(SparqlResultsJsonNames.BnodeUtf8))
        {
            return TermKind.Bnode;
        }

        if(reader.ValueTextEquals(SparqlResultsJsonNames.LiteralUtf8) || reader.ValueTextEquals(SparqlResultsJsonNames.TypedLiteralUtf8))
        {
            return TermKind.Literal;
        }

        if(reader.ValueTextEquals(SparqlResultsJsonNames.TripleUtf8))
        {
            return TermKind.Triple;
        }

        return TermKind.Unknown;
    }

    /// <summary>Classifies the current property-name token of a triple's value wrapper as a component slot.</summary>
    /// <param name="reader">The reader positioned on a property-name token.</param>
    /// <returns>The matched component slot; an unrecognised name maps to the object slot and is overwritten by a real one.</returns>
    private static ComponentSlot ClassifyComponent(ref Utf8JsonReader reader)
    {
        if(reader.ValueTextEquals(SparqlResultsJsonNames.SubjectUtf8))
        {
            return ComponentSlot.Subject;
        }

        if(reader.ValueTextEquals(SparqlResultsJsonNames.PredicateUtf8))
        {
            return ComponentSlot.Predicate;
        }

        return ComponentSlot.Object;
    }

    /// <summary>Copies the current string or property-name token's unescaped UTF-8 into an owned <see cref="Utf8String"/>.</summary>
    /// <param name="reader">The reader positioned on a string or property-name token.</param>
    /// <returns>The decoded value.</returns>
    private static Utf8String ReadUtf8(ref Utf8JsonReader reader)
    {
        int maxLength = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        byte[] buffer = new byte[maxLength];
        int written = reader.CopyString(buffer);

        return new Utf8String(new ReadOnlyMemory<byte>(buffer, 0, written));
    }

    /// <summary>A binding value object's property, classified from its UTF-8 name.</summary>
    private enum PropertyKind
    {
        /// <summary>An unrecognised property, skipped.</summary>
        Unknown,

        /// <summary>The <c>type</c> discriminator.</summary>
        Type,

        /// <summary>The <c>value</c> lexical form or triple wrapper.</summary>
        Value,

        /// <summary>The <c>datatype</c> IRI.</summary>
        Datatype,

        /// <summary>The <c>xml:lang</c> tag.</summary>
        Language,

        /// <summary>The <c>its:dir</c> base direction.</summary>
        Direction
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

        /// <summary>A literal (<c>literal</c> or legacy <c>typed-literal</c>).</summary>
        Literal,

        /// <summary>A quoted triple (<c>triple</c>).</summary>
        Triple
    }

    /// <summary>The triple component a nested child term fills.</summary>
    private enum ComponentSlot
    {
        /// <summary>The triple's subject.</summary>
        Subject,

        /// <summary>The triple's predicate.</summary>
        Predicate,

        /// <summary>The triple's object.</summary>
        Object
    }

    /// <summary>The mutable parse state for one open term object on the deserialization stack.</summary>
    private sealed class TermFrame
    {
        /// <summary>The <c>type</c> discriminator.</summary>
        public TermKind Kind { get; set; }

        /// <summary>The <c>value</c> lexical form (for a leaf term).</summary>
        public Utf8String? Lexical { get; set; }

        /// <summary>The <c>datatype</c> IRI (for a literal).</summary>
        public Utf8String? Datatype { get; set; }

        /// <summary>The <c>xml:lang</c> tag (for a literal).</summary>
        public Utf8String? Language { get; set; }

        /// <summary>The <c>its:dir</c> base direction (for a literal).</summary>
        public Utf8String? Direction { get; set; }

        /// <summary>The built subject term (for a triple).</summary>
        public RdfTerm? Subject { get; set; }

        /// <summary>The built predicate term (for a triple).</summary>
        public RdfTerm? Predicate { get; set; }

        /// <summary>The built object term (for a triple).</summary>
        public RdfTerm? Object { get; set; }

        /// <summary>The component slot the next completed child term fills.</summary>
        public ComponentSlot Pending { get; set; }

        /// <summary>Whether the frame is reading the components inside its triple's <c>value</c> wrapper object.</summary>
        public bool InValue { get; set; }
    }
}
