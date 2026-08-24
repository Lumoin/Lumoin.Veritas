using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>
/// The persist-capture <see cref="ValueSegmentSource"/>: a declared predicate's (subject, value)
/// entries read from a captured default graph's encoded triples through the shared term dictionary,
/// so a persisted value-index snapshot is built from exactly the triples the generation records —
/// consistency by construction, the same discipline the columnar sidecar follows.
/// </summary>
/// <remarks>
/// The lookup never mints ids: a declared predicate the dictionary has not encoded simply has no
/// entries yet. Only literal objects are yielded — a declared value axis indexes literal values, and
/// a non-literal object of the declared predicate is not a value entry.
/// </remarks>
public sealed class CapturedGraphValueSegmentSource: ValueSegmentSource
{
    /// <summary>The captured default-graph triples the entries are read from.</summary>
    private ReadOnlyMemory<EncodedTriple> Triples { get; }

    /// <summary>The shared term dictionary resolving predicate IRIs and value terms.</summary>
    private TermDictionary Dictionary { get; }

    /// <summary>Constructs the source over a captured default graph.</summary>
    /// <param name="triples">The captured default-graph triples.</param>
    /// <param name="dictionary">The shared term dictionary.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <see langword="null"/>.</exception>
    public CapturedGraphValueSegmentSource(ReadOnlyMemory<EncodedTriple> triples, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        Triples = triples;
        Dictionary = dictionary;
    }

    /// <summary>Enumerates the declared predicate's literal-valued entries from the captured triples.</summary>
    /// <param name="predicateIri">The declared predicate's IRI.</param>
    /// <returns>The entries; empty when the predicate is not yet encoded or has no literal objects.</returns>
    public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
    {
        if(!Dictionary.TryGetId(new NamedNode(predicateIri), out TermId predicate))
        {
            yield break;
        }

        for(int i = 0; i < Triples.Length; i++)
        {
            EncodedTriple triple = Triples.Span[i];
            if(triple.Predicate != predicate)
            {
                continue;
            }

            if(Dictionary.Resolve(triple.Object) is Literal value)
            {
                yield return new ValueSegmentEntry(triple.Subject, triple.Object, value);
            }
        }
    }
}
