using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Serializes an expanded JSON-LD object graph (the output of
/// <see cref="JsonLdExpansionTree"/>) to RDF quads, implementing the W3C
/// JSON-LD 1.1 "Deserialize JSON-LD to RDF" algorithm (§10) over the
/// already-expanded form.
/// </summary>
/// <remarks>
/// Driving RDF extraction from the conformant expansion means the full
/// context machinery — scoped contexts, <c>@import</c>, coercions, lists,
/// reverse properties — is applied once, in expansion, and the RDF mapping is
/// a pure projection of the resulting object graph. The walk is iterative over
/// an explicit work stack (no method-call recursion).
/// </remarks>
public static class JsonLdRdfSerializer
{
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";
    private const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";
    private const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";
    private const string RdfJson = "http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string XsdDouble = "http://www.w3.org/2001/XMLSchema#double";
    private const string RdfLangString = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";

    private static long blankNodeCounter;

    /// <summary>
    /// Serializes an expanded JSON-LD object graph to RDF quads.
    /// </summary>
    /// <param name="expanded">The expanded document (an array of node objects).</param>
    /// <param name="pool">Pool for interning UTF-8 term strings.</param>
    /// <returns>The extracted quads in document order.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static List<Quad> Serialize(IReadOnlyList<object?> expanded, Utf8StringPool pool)
    {
        ArgumentNullException.ThrowIfNull(expanded);
        ArgumentNullException.ThrowIfNull(pool);

        SerializationState state = new(pool);
        foreach(object? element in expanded)
        {
            if(element is IReadOnlyDictionary<string, object?> node && !IsValueOrListObject(node))
            {
                state.PushNode(node, graph: null);
            }
        }

        while(state.TryPopNode(out IReadOnlyDictionary<string, object?>? node, out RdfTerm? graph))
        {
            EmitNode(state, node, graph);
        }

        return state.Quads;
    }

    private static void EmitNode(SerializationState state, IReadOnlyDictionary<string, object?> node, RdfTerm? graph)
    {
        //A node whose explicit @id is not a well-formed IRI produces no RDF.
        if(state.SubjectOf(node) is not { } subject)
        {
            return;
        }

        foreach(KeyValuePair<string, object?> property in node)
        {
            string key = property.Key;

            if(JsonLdKeywords.IsType(key))
            {
                NamedNode rdfType = new(state.Pool.Intern(RdfType));
                foreach(object? type in AsList(property.Value))
                {
                    if(type is string typeIri && TryMakeNode(state, typeIri, out RdfTerm? typeTerm))
                    {
                        state.Emit(subject, rdfType, typeTerm, graph);
                    }
                }

                continue;
            }

            if(JsonLdKeywords.IsGraph(key))
            {
                //A node carrying @graph names a graph whose contents are the
                //listed nodes; their quads are emitted under this node's term.
                foreach(object? member in AsList(property.Value))
                {
                    if(member is IReadOnlyDictionary<string, object?> graphNode)
                    {
                        state.PushNode(graphNode, subject);
                    }
                }

                continue;
            }

            if(JsonLdKeywords.IsReverse(key))
            {
                EmitReverse(state, subject, property.Value, graph);
                continue;
            }

            if(JsonLdKeywords.IsIncluded(key))
            {
                //@included carries additional node objects of the same graph;
                //their quads are emitted alongside this node's.
                foreach(object? member in AsList(property.Value))
                {
                    if(member is IReadOnlyDictionary<string, object?> includedNode)
                    {
                        state.PushNode(includedNode, graph);
                    }
                }

                continue;
            }

            //Only absolute-IRI (or blank-node) predicates produce triples; any
            //remaining keyword key carries no RDF.
            if(JsonLdKeywords.IsKeyword(key) || !TryMakeNode(state, key, out RdfTerm? predicateTerm) || predicateTerm is not NamedNode predicate)
            {
                continue;
            }

            foreach(object? value in AsList(property.Value))
            {
                if(TryMakeObject(state, value, graph, out RdfTerm? objectTerm))
                {
                    state.Emit(subject, predicate, objectTerm, graph);
                }
            }
        }
    }

    private static void EmitReverse(SerializationState state, RdfTerm subject, object? reverseValue, RdfTerm? graph)
    {
        if(reverseValue is not IReadOnlyDictionary<string, object?> reverseMap)
        {
            return;
        }

        foreach(KeyValuePair<string, object?> entry in reverseMap)
        {
            if(!TryMakeNode(state, entry.Key, out RdfTerm? predicateTerm) || predicateTerm is not NamedNode predicate)
            {
                continue;
            }

            foreach(object? value in AsList(entry.Value))
            {
                //A reverse property swaps subject and object: the referenced
                //node is the subject and this node is the object.
                if(value is IReadOnlyDictionary<string, object?> reverseNode && state.SubjectOf(reverseNode) is { } reverseSubject)
                {
                    state.PushNode(reverseNode, graph);
                    state.Emit(reverseSubject, predicate, subject, graph);
                }
            }
        }
    }

    private static bool TryMakeObject(SerializationState state, object? value, RdfTerm? graph, out RdfTerm objectTerm)
    {
        switch(value)
        {
            case IReadOnlyDictionary<string, object?> map when map.ContainsKey(JsonLdKeywords.Value):
            {
                return TryMakeLiteral(state, map, out objectTerm);
            }
            case IReadOnlyDictionary<string, object?> map when map.TryGetValue(JsonLdKeywords.List, out object? listValue):
            {
                objectTerm = EmitList(state, listValue, graph);
                return true;
            }
            case IReadOnlyDictionary<string, object?> nodeObject when state.SubjectOf(nodeObject) is { } nodeSubject:
            {
                objectTerm = nodeSubject;
                state.PushNode(nodeObject, graph);
                return true;
            }
            default:
            {
                objectTerm = null!;
                return false;
            }
        }
    }

    private static RdfTerm EmitList(SerializationState state, object? listValue, RdfTerm? graph)
    {
        IReadOnlyList<object?> items = AsList(listValue);
        if(items.Count == 0)
        {
            return new NamedNode(state.Pool.Intern(RdfNil));
        }

        NamedNode rdfFirst = new(state.Pool.Intern(RdfFirst));
        NamedNode rdfRest = new(state.Pool.Intern(RdfRest));
        NamedNode rdfNil = new(state.Pool.Intern(RdfNil));

        //Allocate one blank node per list cell, then thread first/rest links.
        BlankNode[] cells = new BlankNode[items.Count];
        for(int i = 0; i < cells.Length; i++)
        {
            cells[i] = state.MintBlankNode();
        }

        for(int i = 0; i < items.Count; i++)
        {
            if(TryMakeObject(state, items[i], graph, out RdfTerm? item))
            {
                state.Emit(cells[i], rdfFirst, item, graph);
            }

            RdfTerm rest = i + 1 < items.Count ? cells[i + 1] : rdfNil;
            state.Emit(cells[i], rdfRest, rest, graph);
        }

        return cells[0];
    }

    private static bool TryMakeLiteral(SerializationState state, IReadOnlyDictionary<string, object?> valueObject, out RdfTerm literal)
    {
        object? rawValue = valueObject.GetValueOrDefault(JsonLdKeywords.Value);
        string? type = valueObject.GetValueOrDefault(JsonLdKeywords.Type) as string;
        string? language = valueObject.GetValueOrDefault(JsonLdKeywords.Language) as string;

        //A malformed language tag drops the literal's triple.
        if(language is not null && !IsWellFormedLanguage(language))
        {
            literal = null!;
            return false;
        }

        //@json is serialized to its canonical JSON form (RFC 8785: lexicographically
        //ordered object keys, no insignificant whitespace) under rdf:JSON.
        if(JsonLdKeywords.IsJson(type))
        {
            literal = new Literal(state.Pool.Intern(CanonicalJson(rawValue)), new NamedNode(state.Pool.Intern(RdfJson)));
            return true;
        }

        (string lexical, string datatype) = rawValue switch
        {
            bool boolean => (boolean ? "true" : "false", type ?? XsdBoolean),
            long integer => NumberLexical(integer.ToString(CultureInfo.InvariantCulture), type),
            JsonLdJsonNumber number => NumberLexical(number.Raw, type),
            double real => NumberLexical(real.ToString("R", CultureInfo.InvariantCulture), type),
            _ => (LexicalForm(rawValue), type ?? (language is not null ? RdfLangString : XsdString))
        };

        literal = language is not null && type is null
            ? new Literal(state.Pool.Intern(lexical), new NamedNode(state.Pool.Intern(RdfLangString)), state.Pool.Intern(language))
            : new Literal(state.Pool.Intern(lexical), new NamedNode(state.Pool.Intern(datatype)));
        return true;
    }

    /// <summary>
    /// Maps a native JSON number's lexical form to an RDF literal lexical form
    /// and datatype: an integral value with no explicit type is
    /// <c>xsd:integer</c>; any other (or a value coerced to <c>xsd:double</c>)
    /// is the canonical <c>xsd:double</c> lexical form.
    /// </summary>
    private static (string Lexical, string Datatype) NumberLexical(string raw, string? type)
    {
        //A number maps to xsd:integer only when its VALUE is integral and below
        //10^21 (so 10.0 → "10", but 1e21 and 9.9 → xsd:double); the test is on
        //the value, not the lexical token. The datatype is the coercion when one
        //is given, else integer/double by the value's shape; the lexical form is
        //the canonical xsd:double form whenever the value is non-integral or the
        //term coerces to xsd:double (so 9.9 coerced to xsd:integer is "9.9E0").
        bool integerValue = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && !double.IsInfinity(value)
            && value == Math.Truncate(value)
            && Math.Abs(value) < 1e21;

        string datatype = type ?? (integerValue ? XsdInteger : XsdDouble);
        bool useDoubleForm = !integerValue || string.Equals(type, XsdDouble, StringComparison.Ordinal);

        return (useDoubleForm ? CanonicalDouble(raw) : IntegerLexical(value), datatype);
    }

    /// <summary>Formats an integral double as its canonical xsd:integer lexical form (no decimal point or exponent; negative zero normalizes to <c>"0"</c>).</summary>
    private static string IntegerLexical(double value)
    {
        return value == 0 ? "0" : value.ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Produces the canonical <c>xsd:double</c> lexical form (a mantissa with a
    /// single non-zero leading digit, a fractional part, and an <c>E</c>
    /// exponent) from a JSON number token.
    /// </summary>
    private static string CanonicalDouble(string raw)
    {
        if(!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return raw;
        }

        if(value == 0)
        {
            return "0.0E0";
        }

        //The "E+0" custom format yields a single-leading-digit mantissa and a
        //signed exponent (e.g. "4.0E+2"); the canonical xsd:double form drops a
        //positive sign, giving "4.0E2" / "4.0E-2".
        string formatted = value.ToString("0.0###############E+0", CultureInfo.InvariantCulture);
        int eIndex = formatted.IndexOf('E', StringComparison.Ordinal);
        string mantissa = formatted[..eIndex];
        string exponent = formatted[(eIndex + 1)..].TrimStart('+');

        return string.Concat(mantissa, "E", exponent);
    }

    /// <summary>
    /// Serializes a JSON value (the object-graph form of an <c>@json</c> literal)
    /// to canonical JSON (RFC 8785 JCS): object keys sorted by UTF-16 code unit,
    /// no insignificant whitespace, minimal string escaping. The walk is an
    /// explicit output stack — pushed in reverse so popping emits in order — so
    /// there is no method-call recursion over the JSON tree.
    /// </summary>
    private static string CanonicalJson(object? root)
    {
        StringBuilder builder = new();
        Stack<object?> stack = new();
        stack.Push(root);

        while(stack.Count > 0)
        {
            object? item = stack.Pop();
            switch(item)
            {
                case CanonicalToken token:
                {
                    builder.Append(token.Text);
                    break;
                }
                case IReadOnlyDictionary<string, object?> map:
                {
                    List<string> keys = new(map.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    stack.Push(new CanonicalToken("}"));
                    for(int index = keys.Count - 1; index >= 0; index--)
                    {
                        stack.Push(map[keys[index]]);
                        stack.Push(new CanonicalToken(JsonString(keys[index]) + ":"));
                        if(index > 0)
                        {
                            stack.Push(new CanonicalToken(","));
                        }
                    }

                    stack.Push(new CanonicalToken("{"));
                    break;
                }
                case IReadOnlyList<object?> array:
                {
                    stack.Push(new CanonicalToken("]"));
                    for(int index = array.Count - 1; index >= 0; index--)
                    {
                        stack.Push(array[index]);
                        if(index > 0)
                        {
                            stack.Push(new CanonicalToken(","));
                        }
                    }

                    stack.Push(new CanonicalToken("["));
                    break;
                }
                case string text:
                {
                    builder.Append(JsonString(text));
                    break;
                }
                case bool boolean:
                {
                    builder.Append(boolean ? "true" : "false");
                    break;
                }
                case long integer:
                {
                    builder.Append(integer.ToString(CultureInfo.InvariantCulture));
                    break;
                }
                case double real:
                {
                    builder.Append(CanonicalJsonNumber(real.ToString("R", CultureInfo.InvariantCulture)));
                    break;
                }
                case JsonLdJsonNumber number:
                {
                    builder.Append(CanonicalJsonNumber(number.Raw));
                    break;
                }
                default:
                {
                    builder.Append("null");
                    break;
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>Normalizes a JSON number token to its canonical JSON form (an integer literal stays as written; a fractional/exponent value is reduced to the shortest round-tripping decimal).</summary>
    private static string CanonicalJsonNumber(string raw)
    {
        bool integral = raw.IndexOf('.', StringComparison.Ordinal) < 0
            && raw.IndexOf('e', StringComparison.Ordinal) < 0
            && raw.IndexOf('E', StringComparison.Ordinal) < 0;

        if(integral)
        {
            return raw;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : raw;
    }

    /// <summary>JSON-escapes a string and wraps it in quotes, using the minimal escaping RFC 8785 requires.</summary>
    private static string JsonString(string value)
    {
        StringBuilder builder = new(value.Length + 2);
        builder.Append('"');
        foreach(char character in value)
        {
            switch(character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\t': builder.Append("\\t"); break;
                case '\n': builder.Append("\\n"); break;
                case '\f': builder.Append("\\f"); break;
                case '\r': builder.Append("\\r"); break;
                default:
                {
                    if(character < ' ')
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
                }
            }
        }

        builder.Append('"');

        return builder.ToString();
    }

    /// <summary>A pre-rendered literal fragment of canonical JSON output (punctuation or an already-escaped key), distinguished from a JSON string value that still needs escaping.</summary>
    /// <param name="Text">The literal text to append.</param>
    private sealed record CanonicalToken(string Text);

    private static string LexicalForm(object? value)
    {
        return value switch
        {
            string text => text,
            bool boolean => boolean ? "true" : "false",
            long integer => integer.ToString(CultureInfo.InvariantCulture),
            double real => real.ToString("R", CultureInfo.InvariantCulture),
            JsonLdJsonNumber number => number.Raw,
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool TryMakeNode(SerializationState state, string iri, out RdfTerm term)
    {
        if(iri.StartsWith("_:", StringComparison.Ordinal))
        {
            term = new BlankNode(state.Pool.Intern(iri[2..]));
            return true;
        }

        if(IriUtils.IsAbsoluteIri(iri) && IsWellFormedIri(iri))
        {
            term = new NamedNode(state.Pool.Intern(iri));
            return true;
        }

        term = null!;
        return false;
    }

    /// <summary>
    /// Indicates whether an absolute IRI is well-formed enough to appear in a
    /// triple: a malformed IRI (one containing a space, a control character, or
    /// an RFC 3987 excluded delimiter) is rejected, and the triple is dropped.
    /// </summary>
    private static bool IsWellFormedIri(string iri)
    {
        int hashCount = 0;
        foreach(char character in iri)
        {
            if(character <= ' ' || character is '<' or '>' or '"' or '{' or '}' or '|' or '\\' or '^' or '`')
            {
                return false;
            }

            //An IRI has at most one fragment: a second unescaped '#' is malformed.
            if(character == '#' && ++hashCount > 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Indicates whether a language tag is well-formed enough to label a
    /// literal (no spaces or control characters); an invalid tag drops the
    /// literal's triple.
    /// </summary>
    private static bool IsWellFormedLanguage(string language)
    {
        foreach(char character in language)
        {
            if(!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return language.Length > 0;
    }

    private static bool IsValueOrListObject(IReadOnlyDictionary<string, object?> map)
    {
        return map.ContainsKey(JsonLdKeywords.Value) || map.ContainsKey(JsonLdKeywords.List);
    }

    private static IReadOnlyList<object?> AsList(object? value)
    {
        return value as IReadOnlyList<object?> ?? (value is null ? Array.Empty<object?>() : new[] { value });
    }

    /// <summary>
    /// Mutable bookkeeping for one serialization run: the emitted quads, the
    /// node work stack, the per-node-instance subject assignment (so a node is
    /// always the same RDF term), and a once-only processing guard.
    /// </summary>
    private sealed class SerializationState
    {
        private readonly Stack<(IReadOnlyDictionary<string, object?> Node, RdfTerm? Graph)> stack = new();
        private readonly Dictionary<IReadOnlyDictionary<string, object?>, RdfTerm> subjects = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<IReadOnlyDictionary<string, object?>> pushed = new(ReferenceEqualityComparer.Instance);

        /// <summary>Initialises the state with the interning pool.</summary>
        /// <param name="pool">The UTF-8 term pool.</param>
        public SerializationState(Utf8StringPool pool)
        {
            Pool = pool;
        }

        /// <summary>Gets the UTF-8 term-interning pool.</summary>
        public Utf8StringPool Pool { get; }

        /// <summary>Gets the emitted quads.</summary>
        public List<Quad> Quads { get; } = [];

        /// <summary>Records a quad in the graph identified by <paramref name="graph"/> (<see langword="null"/> = default graph).</summary>
        /// <param name="subject">The subject term.</param>
        /// <param name="predicate">The predicate IRI.</param>
        /// <param name="obj">The object term.</param>
        /// <param name="graph">The graph name, or <see langword="null"/> for the default graph.</param>
        public void Emit(RdfTerm subject, NamedNode predicate, RdfTerm obj, RdfTerm? graph)
        {
            Quads.Add(new Quad(subject, predicate, obj, graph));
        }

        /// <summary>Schedules a node for processing once, in graph <paramref name="graph"/>.</summary>
        /// <param name="node">The node object.</param>
        /// <param name="graph">The graph name, or <see langword="null"/>.</param>
        public void PushNode(IReadOnlyDictionary<string, object?> node, RdfTerm? graph)
        {
            if(pushed.Add(node))
            {
                stack.Push((node, graph));
            }
        }

        /// <summary>Pops the next scheduled node.</summary>
        /// <param name="node">The popped node.</param>
        /// <param name="graph">The graph the node belongs to.</param>
        /// <returns><see langword="true"/> when a node was available.</returns>
        public bool TryPopNode(out IReadOnlyDictionary<string, object?> node, out RdfTerm? graph)
        {
            if(stack.Count == 0)
            {
                node = null!;
                graph = null;
                return false;
            }

            (node, graph) = stack.Pop();
            return true;
        }

        /// <summary>Returns the stable RDF subject term for a node instance, minting a blank node when it has no usable <c>@id</c>.</summary>
        /// <param name="node">The node object.</param>
        /// <returns>The subject term.</returns>
        public RdfTerm? SubjectOf(IReadOnlyDictionary<string, object?> node)
        {
            if(subjects.TryGetValue(node, out RdfTerm? existing))
            {
                return existing;
            }

            //A node whose @id KEY is present but is not a well-formed IRI/blank
            //node (a null from a keyword-like @id such as "@ignoreMe", or a
            //malformed IRI) is unusable as a subject and yields no RDF. Only an
            //ABSENT @id makes the node an anonymous blank node.
            if(node.TryGetValue(JsonLdKeywords.Id, out object? idValue))
            {
                if(idValue is string id && TryMakeNode(this, id, out RdfTerm? fromId))
                {
                    subjects[node] = fromId;

                    return fromId;
                }

                return null;
            }

            BlankNode minted = MintBlankNode();
            subjects[node] = minted;

            return minted;
        }

        /// <summary>Mints a fresh, process-unique blank node.</summary>
        /// <returns>The new blank node.</returns>
        public BlankNode MintBlankNode()
        {
            long id = Interlocked.Increment(ref blankNodeCounter);

            return new BlankNode(Pool.Intern($"b{id}"));
        }
    }
}
