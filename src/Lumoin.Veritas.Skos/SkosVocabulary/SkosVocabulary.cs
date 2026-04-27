using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Skos;

/// <summary>
/// IRI constants from the SKOS Simple Knowledge Organization System vocabularies.
/// </summary>
/// <remarks>
/// <para>
/// SKOS Core is defined at http://www.w3.org/2004/02/skos/core# and provides the
/// primary vocabulary for knowledge organization: concepts, concept schemes,
/// collections, labels, and semantic relations.
/// </para>
/// <para>
/// SKOS-XL (Extended Labels) is defined at http://www.w3.org/2008/05/skos-xl# and
/// extends SKOS Core with reified label objects that support structured labeling
/// (annotations, provenance, script variants).
/// </para>
/// <para>
/// All constants are allocated once as static byte arrays wrapping UTF-8 literals.
/// They do not participate in pool allocation and remain valid for application lifetime.
/// </para>
/// </remarks>
public static class SkosVocabulary
{
    /// <summary>
    /// SKOS Core namespace: <c>http://www.w3.org/2004/02/skos/core#</c>.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "SkosVocabulary.Core.Concept is the intended usage pattern.")]
    public static class Core
    {
        /// <summary>The SKOS Core namespace IRI.</summary>
        public const string Namespace = "http://www.w3.org/2004/02/skos/core#";

        //Classes.
        private static readonly byte[] ConceptBytes = "http://www.w3.org/2004/02/skos/core#Concept"u8.ToArray();
        private static readonly byte[] ConceptSchemeBytes = "http://www.w3.org/2004/02/skos/core#ConceptScheme"u8.ToArray();
        private static readonly byte[] CollectionBytes = "http://www.w3.org/2004/02/skos/core#Collection"u8.ToArray();
        private static readonly byte[] OrderedCollectionBytes = "http://www.w3.org/2004/02/skos/core#OrderedCollection"u8.ToArray();

        //Labeling properties.
        private static readonly byte[] PrefLabelBytes = "http://www.w3.org/2004/02/skos/core#prefLabel"u8.ToArray();
        private static readonly byte[] AltLabelBytes = "http://www.w3.org/2004/02/skos/core#altLabel"u8.ToArray();
        private static readonly byte[] HiddenLabelBytes = "http://www.w3.org/2004/02/skos/core#hiddenLabel"u8.ToArray();

        //Documentation properties.
        private static readonly byte[] NoteBytes = "http://www.w3.org/2004/02/skos/core#note"u8.ToArray();
        private static readonly byte[] ChangeNoteBytes = "http://www.w3.org/2004/02/skos/core#changeNote"u8.ToArray();
        private static readonly byte[] DefinitionBytes = "http://www.w3.org/2004/02/skos/core#definition"u8.ToArray();
        private static readonly byte[] EditorialNoteBytes = "http://www.w3.org/2004/02/skos/core#editorialNote"u8.ToArray();
        private static readonly byte[] ExampleBytes = "http://www.w3.org/2004/02/skos/core#example"u8.ToArray();
        private static readonly byte[] HistoryNoteBytes = "http://www.w3.org/2004/02/skos/core#historyNote"u8.ToArray();
        private static readonly byte[] ScopeNoteBytes = "http://www.w3.org/2004/02/skos/core#scopeNote"u8.ToArray();

        //Semantic relation properties.
        private static readonly byte[] SemanticRelationBytes = "http://www.w3.org/2004/02/skos/core#semanticRelation"u8.ToArray();
        private static readonly byte[] BroaderBytes = "http://www.w3.org/2004/02/skos/core#broader"u8.ToArray();
        private static readonly byte[] NarrowerBytes = "http://www.w3.org/2004/02/skos/core#narrower"u8.ToArray();
        private static readonly byte[] RelatedBytes = "http://www.w3.org/2004/02/skos/core#related"u8.ToArray();
        private static readonly byte[] BroaderTransitiveBytes = "http://www.w3.org/2004/02/skos/core#broaderTransitive"u8.ToArray();
        private static readonly byte[] NarrowerTransitiveBytes = "http://www.w3.org/2004/02/skos/core#narrowerTransitive"u8.ToArray();

        //Scheme membership.
        private static readonly byte[] InSchemeBytes = "http://www.w3.org/2004/02/skos/core#inScheme"u8.ToArray();
        private static readonly byte[] HasTopConceptBytes = "http://www.w3.org/2004/02/skos/core#hasTopConcept"u8.ToArray();
        private static readonly byte[] TopConceptOfBytes = "http://www.w3.org/2004/02/skos/core#topConceptOf"u8.ToArray();

        //Collection membership.
        private static readonly byte[] MemberBytes = "http://www.w3.org/2004/02/skos/core#member"u8.ToArray();
        private static readonly byte[] MemberListBytes = "http://www.w3.org/2004/02/skos/core#memberList"u8.ToArray();

        //Mapping properties.
        private static readonly byte[] MappingRelationBytes = "http://www.w3.org/2004/02/skos/core#mappingRelation"u8.ToArray();
        private static readonly byte[] CloseMatchBytes = "http://www.w3.org/2004/02/skos/core#closeMatch"u8.ToArray();
        private static readonly byte[] ExactMatchBytes = "http://www.w3.org/2004/02/skos/core#exactMatch"u8.ToArray();
        private static readonly byte[] BroadMatchBytes = "http://www.w3.org/2004/02/skos/core#broadMatch"u8.ToArray();
        private static readonly byte[] NarrowMatchBytes = "http://www.w3.org/2004/02/skos/core#narrowMatch"u8.ToArray();
        private static readonly byte[] RelatedMatchBytes = "http://www.w3.org/2004/02/skos/core#relatedMatch"u8.ToArray();

        //Notation.
        private static readonly byte[] NotationBytes = "http://www.w3.org/2004/02/skos/core#notation"u8.ToArray();

        /// <summary>The <c>skos:Concept</c> class IRI.</summary>
        public static Utf8String Concept { get; } = new(ConceptBytes);

        /// <summary>The <c>skos:ConceptScheme</c> class IRI.</summary>
        public static Utf8String ConceptScheme { get; } = new(ConceptSchemeBytes);

        /// <summary>The <c>skos:Collection</c> class IRI.</summary>
        public static Utf8String Collection { get; } = new(CollectionBytes);

        /// <summary>The <c>skos:OrderedCollection</c> class IRI.</summary>
        public static Utf8String OrderedCollection { get; } = new(OrderedCollectionBytes);

        /// <summary>The preferred lexical label for a concept in a given language.</summary>
        public static Utf8String PrefLabel { get; } = new(PrefLabelBytes);

        /// <summary>An alternative lexical label for a concept.</summary>
        public static Utf8String AltLabel { get; } = new(AltLabelBytes);

        /// <summary>A label not intended for display but useful for text-based indexing.</summary>
        public static Utf8String HiddenLabel { get; } = new(HiddenLabelBytes);

        /// <summary>A general note for any purpose.</summary>
        public static Utf8String Note { get; } = new(NoteBytes);

        /// <summary>A note about a modification to the concept.</summary>
        public static Utf8String ChangeNote { get; } = new(ChangeNoteBytes);

        /// <summary>A statement of the meaning of a concept.</summary>
        public static Utf8String Definition { get; } = new(DefinitionBytes);

        /// <summary>A note for editors, curators, or maintainers of the scheme.</summary>
        public static Utf8String EditorialNote { get; } = new(EditorialNoteBytes);

        /// <summary>An example of the use of a concept.</summary>
        public static Utf8String Example { get; } = new(ExampleBytes);

        /// <summary>A note about the past state or history of a concept.</summary>
        public static Utf8String HistoryNote { get; } = new(HistoryNoteBytes);

        /// <summary>A note that helps clarify the meaning or use of a concept.</summary>
        public static Utf8String ScopeNote { get; } = new(ScopeNoteBytes);

        /// <summary>A concept that is more general than the subject concept.</summary>
        public static Utf8String Broader { get; } = new(BroaderBytes);

        /// <summary>A concept that is more specific than the subject concept.</summary>
        public static Utf8String Narrower { get; } = new(NarrowerBytes);

        /// <summary>A concept with which the subject concept is associatively related.</summary>
        public static Utf8String Related { get; } = new(RelatedBytes);

        /// <summary>Transitive superproperty of <c>skos:broader</c>.</summary>
        public static Utf8String BroaderTransitive { get; } = new(BroaderTransitiveBytes);

        /// <summary>Transitive superproperty of <c>skos:narrower</c>.</summary>
        public static Utf8String NarrowerTransitive { get; } = new(NarrowerTransitiveBytes);

        /// <summary>A superordinate semantic relation.</summary>
        public static Utf8String SemanticRelation { get; } = new(SemanticRelationBytes);

        /// <summary>Relates a concept to the concept scheme it belongs to.</summary>
        public static Utf8String InScheme { get; } = new(InSchemeBytes);

        /// <summary>Relates a concept scheme to a top-level concept.</summary>
        public static Utf8String HasTopConcept { get; } = new(HasTopConceptBytes);

        /// <summary>Relates a concept to the concept scheme it is a top concept of.</summary>
        public static Utf8String TopConceptOf { get; } = new(TopConceptOfBytes);

        /// <summary>Relates a collection to one of its member concepts or sub-collections.</summary>
        public static Utf8String Member { get; } = new(MemberBytes);

        /// <summary>Relates an ordered collection to an RDF list of its members.</summary>
        public static Utf8String MemberList { get; } = new(MemberListBytes);

        /// <summary>A close but not exact match in another scheme.</summary>
        public static Utf8String CloseMatch { get; } = new(CloseMatchBytes);

        /// <summary>An exact match in another scheme.</summary>
        public static Utf8String ExactMatch { get; } = new(ExactMatchBytes);

        /// <summary>A broader match in another scheme.</summary>
        public static Utf8String BroadMatch { get; } = new(BroadMatchBytes);

        /// <summary>A narrower match in another scheme.</summary>
        public static Utf8String NarrowMatch { get; } = new(NarrowMatchBytes);

        /// <summary>A related match in another scheme.</summary>
        public static Utf8String RelatedMatch { get; } = new(RelatedMatchBytes);

        /// <summary>A superordinate mapping property.</summary>
        public static Utf8String MappingRelation { get; } = new(MappingRelationBytes);

        /// <summary>A notation — a string used to uniquely identify a concept within a scheme.</summary>
        public static Utf8String Notation { get; } = new(NotationBytes);
    }

    /// <summary>
    /// SKOS-XL namespace: <c>http://www.w3.org/2008/05/skos-xl#</c>.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "SkosVocabulary.Xl.Label is the intended usage pattern.")]
    public static class Xl
    {
        /// <summary>The SKOS-XL namespace IRI.</summary>
        public const string Namespace = "http://www.w3.org/2008/05/skos-xl#";

        private static readonly byte[] LabelBytes = "http://www.w3.org/2008/05/skos-xl#Label"u8.ToArray();
        private static readonly byte[] PrefLabelBytes = "http://www.w3.org/2008/05/skos-xl#prefLabel"u8.ToArray();
        private static readonly byte[] AltLabelBytes = "http://www.w3.org/2008/05/skos-xl#altLabel"u8.ToArray();
        private static readonly byte[] HiddenLabelBytes = "http://www.w3.org/2008/05/skos-xl#hiddenLabel"u8.ToArray();
        private static readonly byte[] LiteralFormBytes = "http://www.w3.org/2008/05/skos-xl#literalForm"u8.ToArray();
        private static readonly byte[] LabelRelationBytes = "http://www.w3.org/2008/05/skos-xl#labelRelation"u8.ToArray();

        /// <summary>The <c>skosxl:Label</c> class — a reified label resource.</summary>
        public static Utf8String Label { get; } = new(LabelBytes);

        /// <summary>Relates a concept to a <c>skosxl:Label</c> as its preferred label.</summary>
        public static Utf8String PrefLabel { get; } = new(PrefLabelBytes);

        /// <summary>Relates a concept to a <c>skosxl:Label</c> as an alternative label.</summary>
        public static Utf8String AltLabel { get; } = new(AltLabelBytes);

        /// <summary>Relates a concept to a <c>skosxl:Label</c> as a hidden label.</summary>
        public static Utf8String HiddenLabel { get; } = new(HiddenLabelBytes);

        /// <summary>The literal string form of a <c>skosxl:Label</c>.</summary>
        public static Utf8String LiteralForm { get; } = new(LiteralFormBytes);

        /// <summary>A relationship between two <c>skosxl:Label</c> instances.</summary>
        public static Utf8String LabelRelation { get; } = new(LabelRelationBytes);
    }
}
