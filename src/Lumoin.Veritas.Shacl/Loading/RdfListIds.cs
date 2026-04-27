using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Pre-resolved identifiers for the RDF list vocabulary terms. Used by
/// the path parser to walk sequence-path lists (<c>A/B/C</c>) and
/// alternative-path lists (<c>A|B|C</c>), and by the populator to walk
/// list-valued constraint parameters (<c>sh:in</c>, <c>sh:and</c>,
/// <c>sh:or</c>, <c>sh:xone</c>, <c>sh:languageIn</c>,
/// <c>sh:ignoredProperties</c>, <c>sh:uniqueValuesFor</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RdfFirst"/> and <see cref="RdfRest"/> are predicates
/// (IRIs), so they are typed as <see cref="IriId"/>.
/// <see cref="RdfNil"/> is the terminator term that appears as the
/// object of the final <c>rdf:rest</c> in a list; it is an IRI in
/// practice but is typed as <see cref="TermId"/> because list walkers
/// compare it against the object of <c>rdf:rest</c>, which is typed
/// generically.
/// </para>
/// <para>
/// These three terms are defined in
/// <see href="https://www.w3.org/TR/rdf12-schema/#ch_collectionvocab">RDF 1.2
/// Schema §5.2</see>. They are not part of <see cref="Rdf.RdfsVocabularyIds"/>
/// because that struct is scoped to the terms needed for RDFS inference,
/// not list walking.
/// </para>
/// </remarks>
/// <param name="RdfFirst">The <c>rdf:first</c> predicate identifier.</param>
/// <param name="RdfRest">The <c>rdf:rest</c> predicate identifier.</param>
/// <param name="RdfNil">The <c>rdf:nil</c> terminator identifier.</param>
internal readonly record struct RdfListIds(
    IriId RdfFirst,
    IriId RdfRest,
    TermId RdfNil);
