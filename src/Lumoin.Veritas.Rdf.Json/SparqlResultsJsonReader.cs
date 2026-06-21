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
/// This reader lives in <c>Lumoin.Veritas.Json</c> — the project that owns the <c>System.Text.Json</c> dependency —
/// alongside the other SPARQL Query Results serializations' JSON counterparts; the XML (<c>.srx</c>) reader lives in
/// <c>Lumoin.Veritas.Sparql</c> (no JSON dependency). Both produce the same <see cref="SparqlResultSet"/> model.
/// </para>
/// <para>
/// Binding value objects carry a <c>type</c> (<c>uri</c>, <c>literal</c>, the legacy <c>typed-literal</c>,
/// <c>bnode</c>, or <c>triple</c>) and a <c>value</c>. A <c>literal</c> is typed <c>rdf:langString</c> when it
/// carries <c>xml:lang</c>, its declared <c>datatype</c>, or <c>xsd:string</c> otherwise. A <c>triple</c>'s
/// <c>value</c> is an object with nested <c>subject</c>/<c>predicate</c>/<c>object</c> term objects; it parses to a
/// <see cref="TripleTerm"/> over an explicit stack, so deep nesting cannot overflow the call stack.
/// </para>
/// </remarks>
public static class SparqlResultsJsonReader
{
    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    private static NamedNode RdfLangString { get; } = new(Vocabulary.Rdf.LangString);

    private static NamedNode RdfDirLangString { get; } = new(Vocabulary.Rdf.DirLangString);

    /// <summary>Reads a result set from the in-memory JSON bytes.</summary>
    /// <param name="bytes">The <c>.srj</c> document bytes.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results JSON.</exception>
    public static SparqlResultSet Read(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            return ReadDocument(document);
        }
        catch(JsonException ex)
        {
            throw new FormatException($"Malformed SPARQL Results JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Reads a result set from a possibly-segmented in-memory JSON byte sequence, such as a pipe's buffer, with no contiguous copy.</summary>
    /// <param name="bytes">The <c>.srj</c> document bytes, possibly spanning segments.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results JSON.</exception>
    public static SparqlResultSet Read(ReadOnlySequence<byte> bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            return ReadDocument(document);
        }
        catch(JsonException ex)
        {
            throw new FormatException($"Malformed SPARQL Results JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Reads a result set from a JSON stream.</summary>
    /// <param name="stream">The stream over the <c>.srj</c> document.</param>
    /// <returns>The parsed result set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The document is not well-formed SPARQL Results JSON.</exception>
    public static SparqlResultSet Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using JsonDocument document = JsonDocument.Parse(stream);
            return ReadDocument(document);
        }
        catch(JsonException ex)
        {
            throw new FormatException($"Malformed SPARQL Results JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Builds the result set from the parsed JSON document.</summary>
    /// <param name="document">The parsed document.</param>
    /// <returns>The result set.</returns>
    /// <exception cref="FormatException">The document does not have the SPARQL-results shape.</exception>
    private static SparqlResultSet ReadDocument(JsonDocument document)
    {
        JsonElement root = document.RootElement;
        if(root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("SPARQL Results JSON must be a top-level object.");
        }

        if(root.TryGetProperty(SparqlResultsJsonNames.Boolean, out JsonElement boolean))
        {
            return SparqlResultSet.ForAsk(ReadBoolean(boolean));
        }

        List<Utf8String> variables = ReadHeadVariables(root);
        if(!root.TryGetProperty(SparqlResultsJsonNames.Results, out JsonElement results) || !results.TryGetProperty(SparqlResultsJsonNames.Bindings, out JsonElement bindings) || bindings.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("SPARQL SELECT results JSON is missing its results.bindings array.");
        }

        List<SparqlSolution> solutions = [];
        foreach(JsonElement row in bindings.EnumerateArray())
        {
            solutions.Add(ReadSolution(row));
        }

        return SparqlResultSet.ForSelect(variables, solutions);
    }

    /// <summary>Reads the head's declared variables (<c>head.vars</c>) in document order; empty when absent.</summary>
    /// <param name="root">The root object.</param>
    /// <returns>The variable names.</returns>
    private static List<Utf8String> ReadHeadVariables(JsonElement root)
    {
        List<Utf8String> variables = [];
        if(!root.TryGetProperty(SparqlResultsJsonNames.Head, out JsonElement head) || !head.TryGetProperty(SparqlResultsJsonNames.Vars, out JsonElement vars) || vars.ValueKind != JsonValueKind.Array)
        {
            return variables;
        }

        foreach(JsonElement name in vars.EnumerateArray())
        {
            if(name.ValueKind == JsonValueKind.String)
            {
                variables.Add(Utf8String.FromString(name.GetString()!));
            }
        }

        return variables;
    }

    /// <summary>Reads one binding-set object into a solution, binding each named property to its parsed term.</summary>
    /// <param name="row">The binding-set object.</param>
    /// <returns>The solution mapping.</returns>
    private static SparqlSolution ReadSolution(JsonElement row)
    {
        if(row.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A SPARQL Results JSON binding set must be an object.");
        }

        List<SparqlBinding> bindings = [];
        foreach(JsonProperty binding in row.EnumerateObject())
        {
            bindings.Add(new SparqlBinding(new SparqlVariable(Utf8String.FromString(binding.Name)), ParseTerm(binding.Value)));
        }

        return new SparqlSolution(bindings);
    }

    /// <summary>
    /// Parses a binding value object to an RDF term over an explicit post-order work stack, so a deeply-nested
    /// triple term cannot overflow the call stack. The reachable depth is already bounded by
    /// <see cref="JsonDocument"/>'s own nesting cap (a triple term nests roughly two JSON object levels per
    /// quoted-triple level, so deeper input throws <see cref="System.Text.Json.JsonException"/> at parse before
    /// this walk runs); no further quoted-triple cap is enforced here.
    /// </summary>
    /// <param name="value">The binding value object.</param>
    /// <returns>The parsed term.</returns>
    private static RdfTerm ParseTerm(JsonElement value)
    {
        Dictionary<int, RdfTerm> results = [];
        Stack<TermFrame> work = new();
        int nextId = 0;
        TermFrame rootFrame = new() { Element = value, Id = nextId++ };
        work.Push(rootFrame);

        while(work.Count > 0)
        {
            TermFrame frame = work.Pop();
            if(!IsTriple(frame.Element))
            {
                results[frame.Id] = ParseLeaf(frame.Element);
                continue;
            }

            if(frame.Combine)
            {
                if(results[frame.PredicateId] is not NamedNode predicate)
                {
                    throw new FormatException("A SPARQL Results JSON triple value has a non-IRI predicate term.");
                }

                results[frame.Id] = new TripleTerm(results[frame.SubjectId], predicate, results[frame.ObjectId]);
                continue;
            }

            JsonElement inner = frame.Element.GetProperty(SparqlResultsJsonNames.Value);
            TermFrame subject = new() { Element = Component(inner, SparqlResultsJsonNames.Subject), Id = nextId++ };
            TermFrame predicateFrame = new() { Element = Component(inner, SparqlResultsJsonNames.Predicate), Id = nextId++ };
            TermFrame @object = new() { Element = Component(inner, SparqlResultsJsonNames.Object), Id = nextId++ };
            frame.Combine = true;
            frame.SubjectId = subject.Id;
            frame.PredicateId = predicateFrame.Id;
            frame.ObjectId = @object.Id;
            work.Push(frame);
            work.Push(@object);
            work.Push(predicateFrame);
            work.Push(subject);
        }

        return results[rootFrame.Id];
    }

    /// <summary>Returns whether a binding value object is a triple term (<c>"type":"triple"</c>).</summary>
    /// <param name="value">The value object.</param>
    /// <returns><see langword="true"/> when it is a triple term.</returns>
    private static bool IsTriple(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(SparqlResultsJsonNames.Type, out JsonElement type)
            && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), SparqlResultsJsonNames.Triple, StringComparison.Ordinal);
    }

    /// <summary>Returns the nested term object for a triple component (<c>subject</c>/<c>predicate</c>/<c>object</c>).</summary>
    /// <param name="tripleValue">The triple's <c>value</c> object.</param>
    /// <param name="component">The component property name.</param>
    /// <returns>The component term object.</returns>
    private static JsonElement Component(JsonElement tripleValue, string component)
    {
        if(!tripleValue.TryGetProperty(component, out JsonElement element))
        {
            throw new FormatException($"A SPARQL Results JSON triple value is missing its '{component}'.");
        }

        return element;
    }

    /// <summary>Parses a leaf value object (<c>uri</c>/<c>literal</c>/<c>typed-literal</c>/<c>bnode</c>) to its RDF term.</summary>
    /// <param name="value">The value object.</param>
    /// <returns>The parsed term.</returns>
    private static RdfTerm ParseLeaf(JsonElement value)
    {
        if(value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A SPARQL Results JSON binding value must be an object.");
        }

        string type = RequireString(value, SparqlResultsJsonNames.Type);
        string lexical = RequireString(value, SparqlResultsJsonNames.Value);

        return type switch
        {
            SparqlResultsJsonNames.Uri => new NamedNode(Utf8String.FromString(lexical)),
            SparqlResultsJsonNames.Bnode => new BlankNode(Utf8String.FromString(lexical)),
            SparqlResultsJsonNames.Literal or SparqlResultsJsonNames.TypedLiteral => ParseLiteral(value, lexical),
            _ => throw new FormatException($"Unsupported SPARQL Results JSON binding value type '{type}'.")
        };
    }

    /// <summary>Parses a literal value: <c>rdf:dirLangString</c> when it carries both <c>xml:lang</c> and <c>its:dir</c> (RDF 1.2), <c>rdf:langString</c> for <c>xml:lang</c> alone, its declared <c>datatype</c>, or <c>xsd:string</c> otherwise.</summary>
    /// <param name="value">The value object.</param>
    /// <param name="lexical">The literal's lexical form.</param>
    /// <returns>The literal term.</returns>
    private static Literal ParseLiteral(JsonElement value, string lexical)
    {
        if(value.TryGetProperty(SparqlResultsJsonNames.Language, out JsonElement language) && language.ValueKind == JsonValueKind.String)
        {
            Utf8String tag = Utf8String.FromString(language.GetString()!);
            if(value.TryGetProperty(SparqlResultsJsonNames.Direction, out JsonElement direction) && direction.ValueKind == JsonValueKind.String && TextDirections.TryParse(direction.GetString(), out TextDirection baseDirection))
            {
                return new Literal(Utf8String.FromString(lexical), RdfDirLangString, tag, baseDirection);
            }

            return new Literal(Utf8String.FromString(lexical), RdfLangString, tag);
        }

        if(value.TryGetProperty(SparqlResultsJsonNames.Datatype, out JsonElement datatype) && datatype.ValueKind == JsonValueKind.String)
        {
            return new Literal(Utf8String.FromString(lexical), new NamedNode(Utf8String.FromString(datatype.GetString()!)));
        }

        return new Literal(Utf8String.FromString(lexical), XsdString);
    }

    /// <summary>Reads the <c>boolean</c> of an <c>ASK</c> result, accepting a JSON boolean or the strings <c>true</c>/<c>false</c>.</summary>
    /// <param name="boolean">The <c>boolean</c> element.</param>
    /// <returns>The boolean answer.</returns>
    /// <exception cref="FormatException">The value is neither a JSON boolean nor <c>true</c>/<c>false</c>.</exception>
    private static bool ReadBoolean(JsonElement boolean)
    {
        return boolean.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when string.Equals(boolean.GetString(), "true", StringComparison.Ordinal) => true,
            JsonValueKind.String when string.Equals(boolean.GetString(), "false", StringComparison.Ordinal) => false,
            _ => throw new FormatException("A SPARQL ASK result's 'boolean' must be true or false.")
        };
    }

    /// <summary>Reads a required string property from a value object.</summary>
    /// <param name="value">The value object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The string value.</returns>
    /// <exception cref="FormatException">The property is missing or not a string.</exception>
    private static string RequireString(JsonElement value, string name)
    {
        if(!value.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"A SPARQL Results JSON binding value is missing its string '{name}'.");
        }

        return property.GetString()!;
    }

    /// <summary>One node on the explicit term-parse stack: its JSON element, its result id, and (for a triple) the ids of its parsed components.</summary>
    private sealed class TermFrame
    {
        /// <summary>The JSON element this frame parses.</summary>
        public JsonElement Element { get; init; }

        /// <summary>The id under which this frame's parsed term is stored.</summary>
        public int Id { get; init; }

        /// <summary>Whether the frame is in its combine phase (its triple components are parsed).</summary>
        public bool Combine { get; set; }

        /// <summary>The result id of a triple's subject term.</summary>
        public int SubjectId { get; set; }

        /// <summary>The result id of a triple's predicate term.</summary>
        public int PredicateId { get; set; }

        /// <summary>The result id of a triple's object term.</summary>
        public int ObjectId { get; set; }
    }
}
