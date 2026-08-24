using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;

namespace Lumoin.Veritas.Database.Completion;

/// <summary>
/// The fixed RDF-vocabulary corpus an editor's completion offers in a Turtle / SHACL / OWL / SPARQL buffer.
/// The core groups are the SHACL (<c>sh:</c>), OWL (<c>owl:</c>), RDF (<c>rdf:</c>), RDFS (<c>rdfs:</c>), and
/// XSD-datatype (<c>xsd:</c>) terms, projected to prefixed-name (CURIE) JSON; the IRIs come from the
/// canonical vocabulary constants, so the corpus has one source of truth, and the conventional prefixes are
/// the ones a buffer almost always declares. A composing host contributes further prefix-paired groups and
/// the value-datatype IRIs its own composition registered, so a domain vocabulary reaches the corpus without
/// this assembly depending on it.
/// </summary>
public static class EditorVocabulary
{
    /// <summary>The common RDFS terms a shapes / schema author reaches for.</summary>
    private static IReadOnlyList<Utf8String> RdfsTerms { get; } =
    [
        RdfVocabulary.Rdfs.Class, RdfVocabulary.Rdfs.Datatype, RdfVocabulary.Rdfs.SubClassOf,
        RdfVocabulary.Rdfs.SubPropertyOf, RdfVocabulary.Rdfs.Domain, RdfVocabulary.Rdfs.Range,
        RdfVocabulary.Rdfs.Label, RdfVocabulary.Rdfs.Comment,
    ];

    /// <summary>The common XSD datatypes a <c>sh:datatype</c> value or a typed literal uses.</summary>
    private static IReadOnlyList<Utf8String> XsdTerms { get; } =
    [
        Vocabulary.Xsd.String, Vocabulary.Xsd.Boolean, Vocabulary.Xsd.Integer, Vocabulary.Xsd.Decimal,
        Vocabulary.Xsd.Double, Vocabulary.Xsd.DateTime, Vocabulary.Xsd.Date, Vocabulary.Xsd.Time,
        Vocabulary.Xsd.AnyUri,
    ];

    /// <summary>The common RDF terms an ontology / schema author reaches for: the type predicate, the property and list classes, and the list vocabulary.</summary>
    private static IReadOnlyList<Utf8String> RdfTerms { get; } =
    [
        Vocabulary.Rdf.Type, RdfVocabulary.Rdf.Property, RdfVocabulary.Rdf.Value, Vocabulary.Rdf.LangString,
        RdfVocabulary.Rdf.List, RdfVocabulary.Rdf.First, RdfVocabulary.Rdf.Rest, RdfVocabulary.Rdf.Nil,
    ];

    /// <summary>The conventional-prefix + term-set groups this assembly's own vocabularies make up.</summary>
    private static IReadOnlyList<(string Prefix, IReadOnlyList<Utf8String> Terms)> CoreGroups { get; } =
    [
        ("sh", ShaclCoreVocabulary.All),
        ("sh", ShaclConstraintVocabulary.All),
        ("owl", OwlVocabulary.All),
        ("rdf", RdfTerms),
        ("rdfs", RdfsTerms),
        ("xsd", XsdTerms)
    ];

    /// <summary>
    /// Serializes the corpus as a JSON array of completion candidates, e.g. <c>["sh:NodeShape", …]</c>. The
    /// core groups render first, then the contributed groups in the order they were handed, each term as a
    /// prefixed name; then the registered datatype IRIs no group already covers, in ordinal byte order, each
    /// as an angle-bracketed full IRI. A datatype IRI that is one of the handed group terms is dropped, because
    /// that term already rides the array as its group's prefixed name, and one rendered candidate never
    /// appears twice.
    /// </summary>
    /// <param name="additionalGroups">The conventional-prefix + term-set groups the composing host contributes.</param>
    /// <param name="datatypeIris">The value-datatype IRIs the host's composition registered.</param>
    /// <returns>The candidate JSON array.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="additionalGroups"/> or <paramref name="datatypeIris"/> is <see langword="null"/>.</exception>
    public static string ToJson(IReadOnlyList<(string Prefix, IReadOnlyList<Utf8String> Terms)> additionalGroups, IReadOnlyCollection<Utf8String> datatypeIris)
    {
        ArgumentNullException.ThrowIfNull(additionalGroups);
        ArgumentNullException.ThrowIfNull(datatypeIris);

        HashSet<Utf8String> groupTerms = [];
        HashSet<string> rendered = [];
        StringBuilder candidate = new();
        StringBuilder json = new();
        json.Append('[');

        bool first = true;
        AppendGroups(json, CoreGroups, candidate, groupTerms, rendered, ref first);
        AppendGroups(json, additionalGroups, candidate, groupTerms, rendered, ref first);
        AppendUncoveredDatatypes(json, datatypeIris, candidate, groupTerms, rendered, ref first);

        json.Append(']');

        return json.ToString();
    }

    /// <summary>Appends every group's terms as prefixed-name candidates, recording each term's IRI and each rendered candidate so the datatype lane and the later groups can both see them.</summary>
    /// <param name="jsonToAppendTo">The array being built.</param>
    /// <param name="groups">The prefix-paired groups to render.</param>
    /// <param name="candidate">The scratch buffer one candidate is composed in.</param>
    /// <param name="groupTermsToAppendTo">The IRIs every rendered group carries.</param>
    /// <param name="renderedToAppendTo">The candidates already in the array.</param>
    /// <param name="first">Whether the next candidate is the first in the array.</param>
    private static void AppendGroups(StringBuilder jsonToAppendTo, IReadOnlyList<(string Prefix, IReadOnlyList<Utf8String> Terms)> groups, StringBuilder candidate, HashSet<Utf8String> groupTermsToAppendTo, HashSet<string> renderedToAppendTo, ref bool first)
    {
        foreach((string prefix, IReadOnlyList<Utf8String> terms) in groups)
        {
            foreach(Utf8String iri in terms)
            {
                groupTermsToAppendTo.Add(iri);
                candidate.Clear();
                candidate.Append(prefix).Append(':').Append(LocalName(iri));
                string text = candidate.ToString();
                if(!renderedToAppendTo.Add(text))
                {
                    continue;
                }

                AppendSeparator(jsonToAppendTo, ref first);

                //Local names are bare identifiers (no JSON metacharacters), so the CURIE needs no escaping.
                jsonToAppendTo.Append('"').Append(text).Append('"');
            }
        }
    }

    /// <summary>Appends every registered datatype IRI no rendered group already carries, in ordinal byte order, as an angle-bracketed full-IRI candidate.</summary>
    /// <param name="jsonToAppendTo">The array being built.</param>
    /// <param name="datatypeIris">The registered datatype IRIs.</param>
    /// <param name="candidate">The scratch buffer one candidate is composed in.</param>
    /// <param name="groupTerms">The IRIs the rendered groups carry.</param>
    /// <param name="renderedToAppendTo">The candidates already in the array.</param>
    /// <param name="first">Whether the next candidate is the first in the array.</param>
    private static void AppendUncoveredDatatypes(StringBuilder jsonToAppendTo, IReadOnlyCollection<Utf8String> datatypeIris, StringBuilder candidate, HashSet<Utf8String> groupTerms, HashSet<string> renderedToAppendTo, ref bool first)
    {
        List<Utf8String> uncovered = new(datatypeIris.Count);
        foreach(Utf8String iri in datatypeIris)
        {
            if(!groupTerms.Contains(iri))
            {
                uncovered.Add(iri);
            }
        }

        uncovered.Sort(CompareOrdinal);

        foreach(Utf8String iri in uncovered)
        {
            candidate.Clear();
            candidate.Append('<');
            AppendJsonEscaped(candidate, iri.ToString());
            candidate.Append('>');
            string text = candidate.ToString();
            if(!renderedToAppendTo.Add(text))
            {
                continue;
            }

            AppendSeparator(jsonToAppendTo, ref first);
            jsonToAppendTo.Append('"').Append(text).Append('"');
        }
    }

    /// <summary>Orders two IRIs by their UTF-8 bytes, so the full-IRI lane's candidates answer in one deterministic order whatever order the registry enumerated them in.</summary>
    /// <param name="left">The first IRI.</param>
    /// <param name="right">The second IRI.</param>
    /// <returns>A negative value when <paramref name="left"/> sorts first, a positive value when <paramref name="right"/> does, and zero when the bytes are equal.</returns>
    private static int CompareOrdinal(Utf8String left, Utf8String right)
    {
        return left.Span.SequenceCompareTo(right.Span);
    }

    /// <summary>Appends a comma before every candidate after the first, then clears the first-candidate flag.</summary>
    /// <param name="jsonToAppendTo">The array being built.</param>
    /// <param name="first">Whether the next candidate is the first in the array.</param>
    private static void AppendSeparator(StringBuilder jsonToAppendTo, ref bool first)
    {
        if(!first)
        {
            jsonToAppendTo.Append(',');
        }

        first = false;
    }

    /// <summary>Appends <paramref name="value"/> escaped per RFC 8259, without the surrounding quotes.</summary>
    /// <param name="builderToAppendTo">The buffer the escaped text is appended to.</param>
    /// <param name="value">The raw value.</param>
    private static void AppendJsonEscaped(StringBuilder builderToAppendTo, string value)
    {
        foreach(char character in value)
        {
            builderToAppendTo.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        }
    }

    /// <summary>
    /// The local name of a vocabulary IRI: the part after the last <c>#</c> or <c>/</c>, whichever stands
    /// later, else the whole IRI. Hash namespaces (<c>sh:</c>, <c>owl:</c>) and slash namespaces both resolve
    /// through the one rule.
    /// </summary>
    /// <param name="iri">The vocabulary IRI.</param>
    /// <returns>The local name.</returns>
    private static string LocalName(Utf8String iri)
    {
        string text = iri.ToString();
        int separator = Math.Max(text.LastIndexOf('#'), text.LastIndexOf('/'));

        return separator >= 0 && separator < text.Length - 1 ? text[(separator + 1)..] : text;
    }
}
