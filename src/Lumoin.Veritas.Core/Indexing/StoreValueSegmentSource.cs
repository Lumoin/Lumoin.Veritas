using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>
/// The engine-side <see cref="ValueSegmentSource"/>: a declared predicate's (subject, value) entries read
/// from a pinned <see cref="HypertrieGraphStore"/> generation through the shared term dictionary.
/// </summary>
/// <remarks>
/// The lookup never mints ids: a declared predicate the dictionary has not encoded simply has no entries
/// yet. Only literal objects are yielded — a declared value axis indexes literal values, and a non-literal
/// object of the declared predicate is not a value entry.
/// </remarks>
public sealed class StoreValueSegmentSource: ValueSegmentSource
{
    /// <summary>The pinned store generation the build reads.</summary>
    private HypertrieGraphStore Store { get; }

    /// <summary>The shared term dictionary resolving predicate IRIs and value terms.</summary>
    private TermDictionary Dictionary { get; }

    /// <summary>Constructs the source over a pinned generation.</summary>
    /// <param name="store">The pinned store generation.</param>
    /// <param name="dictionary">The shared term dictionary.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public StoreValueSegmentSource(HypertrieGraphStore store, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);

        Store = store;
        Dictionary = dictionary;
    }

    /// <summary>Enumerates the declared predicate's literal-valued entries from the pinned generation.</summary>
    /// <param name="predicateIri">The declared predicate's IRI.</param>
    /// <returns>The entries; empty when the predicate is not yet encoded or has no literal objects.</returns>
    public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
    {
        if(!Dictionary.TryGetId(new NamedNode(predicateIri), out TermId predicate))
        {
            yield break;
        }

        foreach(EncodedTriple triple in Store.Match(predicate: predicate, subject: TermId.None, @object: TermId.None))
        {
            if(Dictionary.Resolve(triple.Object) is Literal value)
            {
                yield return new ValueSegmentEntry(triple.Subject, triple.Object, value);
            }
        }
    }
}
