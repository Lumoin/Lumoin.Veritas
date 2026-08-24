using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Well-known IRI constants from the RDF and RDFS vocabularies used by
/// graph computation, RDFS inference, property path evaluation, and
/// RDF collection traversal.
/// </summary>
/// <remarks>
/// <para>
/// These are allocated once as static byte arrays. Since <see cref="Utf8String"/>
/// is a struct wrapping <see cref="ReadOnlyMemory{T}"/>, these do not participate
/// in pool allocation and remain valid for the lifetime of the application.
/// </para>
/// <para>
/// The data-model terms (<c>rdf:type</c>, all RDF-defined datatypes, <c>rdf:reifies</c>)
/// remain in <see cref="Vocabulary"/> in Core. This class contains the RDF and RDFS
/// vocabulary terms that have computational semantics: list traversal, classic
/// reification, property/class hierarchy, and annotation properties.
/// </para>
/// </remarks>
public static class RdfVocabulary
{
    /// <summary>
    /// RDF namespace terms beyond the data-model set in Core.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Includes list vocabulary (<c>rdf:first</c>, <c>rdf:rest</c>, <c>rdf:nil</c>,
    /// <c>rdf:List</c>), classic reification (<c>rdf:Statement</c>, <c>rdf:subject</c>,
    /// <c>rdf:predicate</c>, <c>rdf:object</c>), and the <c>rdf:Property</c> and
    /// <c>rdf:value</c> terms.
    /// </para>
    /// <para>
    /// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/">RDF 1.2 Concepts</see>
    /// and <see href="https://www.w3.org/TR/rdf12-schema/#ch_summary">RDF 1.2 Schema §6</see>.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "RdfVocabulary.Rdf.First is the intended usage pattern.")]
    public static class Rdf
    {
        /// <summary>The RDF namespace IRI.</summary>
        public const string Namespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

        //Class and property terms.
        private static byte[] PropertyBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#Property"u8.ToArray();
        private static byte[] ValueBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#value"u8.ToArray();

        //Classic reification.
        private static byte[] StatementBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#Statement"u8.ToArray();
        private static byte[] SubjectPropBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#subject"u8.ToArray();
        private static byte[] PredicatePropBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#predicate"u8.ToArray();
        private static byte[] ObjectPropBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#object"u8.ToArray();

        //List vocabulary.
        private static byte[] FirstBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8.ToArray();
        private static byte[] RestBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8.ToArray();
        private static byte[] NilBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"u8.ToArray();
        private static byte[] ListBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#List"u8.ToArray();

        /// <summary>The <c>rdf:Property</c> class IRI.</summary>
        public static Utf8String Property { get; } = new(PropertyBytes);

        /// <summary>The <c>rdf:value</c> property IRI.</summary>
        public static Utf8String Value { get; } = new(ValueBytes);

        /// <summary>The <c>rdf:Statement</c> class for classic reification.</summary>
        public static Utf8String Statement { get; } = new(StatementBytes);

        /// <summary>The <c>rdf:subject</c> predicate for classic reification.</summary>
        public static Utf8String SubjectProp { get; } = new(SubjectPropBytes);

        /// <summary>The <c>rdf:predicate</c> predicate for classic reification.</summary>
        public static Utf8String PredicateProp { get; } = new(PredicatePropBytes);

        /// <summary>The <c>rdf:object</c> predicate for classic reification.</summary>
        public static Utf8String ObjectProp { get; } = new(ObjectPropBytes);

        /// <summary>The <c>rdf:first</c> predicate for RDF list linked-list structure.</summary>
        public static Utf8String First { get; } = new(FirstBytes);

        /// <summary>The <c>rdf:rest</c> predicate for RDF list linked-list structure.</summary>
        public static Utf8String Rest { get; } = new(RestBytes);

        /// <summary>The <c>rdf:nil</c> resource terminating an RDF list.</summary>
        public static Utf8String Nil { get; } = new(NilBytes);

        /// <summary>The <c>rdf:List</c> class IRI for RDF collections.</summary>
        public static Utf8String List { get; } = new(ListBytes);

        /// <summary>
        /// The shared <see cref="NamedNode"/> instances of the list and classic
        /// reification terms, so hot parse paths reuse one node per term instead of
        /// wrapping the IRI per use. Instance sharing is observationally free —
        /// <see cref="NamedNode"/> is an immutable record with value equality.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "RdfVocabulary.Rdf.Nodes.First is the intended usage pattern.")]
        public static class Nodes
        {
            /// <summary>The shared <c>rdf:Statement</c> class node.</summary>
            public static NamedNode Statement { get; } = new(Rdf.Statement);

            /// <summary>The shared <c>rdf:subject</c> predicate node.</summary>
            public static NamedNode SubjectProp { get; } = new(Rdf.SubjectProp);

            /// <summary>The shared <c>rdf:predicate</c> predicate node.</summary>
            public static NamedNode PredicateProp { get; } = new(Rdf.PredicateProp);

            /// <summary>The shared <c>rdf:object</c> predicate node.</summary>
            public static NamedNode ObjectProp { get; } = new(Rdf.ObjectProp);

            /// <summary>The shared <c>rdf:first</c> predicate node.</summary>
            public static NamedNode First { get; } = new(Rdf.First);

            /// <summary>The shared <c>rdf:rest</c> predicate node.</summary>
            public static NamedNode Rest { get; } = new(Rdf.Rest);

            /// <summary>The shared <c>rdf:nil</c> resource node.</summary>
            public static NamedNode Nil { get; } = new(Rdf.Nil);
        }
    }

    /// <summary>
    /// The RDFS namespace: <c>http://www.w3.org/2000/01/rdf-schema#</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RDFS terms are used by RDFS inference (<c>rdfs:subClassOf</c>, <c>rdfs:subPropertyOf</c>,
    /// <c>rdfs:domain</c>, <c>rdfs:range</c>) and by SHACL validation (<c>rdfs:Class</c>,
    /// <c>rdfs:label</c>).
    /// </para>
    /// <para>
    /// Defined in <see href="https://www.w3.org/TR/rdf12-schema/">RDF 1.2 Schema</see>.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "RdfVocabulary.Rdfs.Class is the intended usage pattern.")]
    public static class Rdfs
    {
        /// <summary>The RDFS namespace IRI.</summary>
        public const string Namespace = "http://www.w3.org/2000/01/rdf-schema#";

        //Classes.
        private static byte[] ResourceBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#Resource"u8.ToArray();
        private static byte[] ClassBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#Class"u8.ToArray();
        private static byte[] LiteralClassBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#Literal"u8.ToArray();
        private static byte[] DatatypeBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#Datatype"u8.ToArray();
        private static byte[] ContainerBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#Container"u8.ToArray();
        private static byte[] ContainerMembershipPropertyBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#ContainerMembershipProperty"u8.ToArray();

        //Properties.
        private static byte[] SubClassOfBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#subClassOf"u8.ToArray();
        private static byte[] SubPropertyOfBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#subPropertyOf"u8.ToArray();
        private static byte[] DomainBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#domain"u8.ToArray();
        private static byte[] RangeBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#range"u8.ToArray();
        private static byte[] LabelBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#label"u8.ToArray();
        private static byte[] CommentBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#comment"u8.ToArray();
        private static byte[] SeeAlsoBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#seeAlso"u8.ToArray();
        private static byte[] IsDefinedByBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#isDefinedBy"u8.ToArray();
        private static byte[] MemberBytes { get; } = "http://www.w3.org/2000/01/rdf-schema#member"u8.ToArray();

        /// <summary>The <c>rdfs:Resource</c> class — the class of everything.</summary>
        public static Utf8String Resource { get; } = new(ResourceBytes);

        /// <summary>The <c>rdfs:Class</c> class — the class of all classes.</summary>
        public static Utf8String Class { get; } = new(ClassBytes);

        /// <summary>The <c>rdfs:Literal</c> class — the class of literal values.</summary>
        public static Utf8String LiteralClass { get; } = new(LiteralClassBytes);

        /// <summary>The <c>rdfs:Datatype</c> class — the class of datatypes.</summary>
        public static Utf8String Datatype { get; } = new(DatatypeBytes);

        /// <summary>The <c>rdfs:Container</c> class.</summary>
        public static Utf8String Container { get; } = new(ContainerBytes);

        /// <summary>The <c>rdfs:ContainerMembershipProperty</c> class.</summary>
        public static Utf8String ContainerMembershipProperty { get; } = new(ContainerMembershipPropertyBytes);

        /// <summary>The <c>rdfs:subClassOf</c> property — relates a class to its superclass.</summary>
        public static Utf8String SubClassOf { get; } = new(SubClassOfBytes);

        /// <summary>The <c>rdfs:subPropertyOf</c> property — relates a property to its superproperty.</summary>
        public static Utf8String SubPropertyOf { get; } = new(SubPropertyOfBytes);

        /// <summary>The <c>rdfs:domain</c> property — the domain of a property.</summary>
        public static Utf8String Domain { get; } = new(DomainBytes);

        /// <summary>The <c>rdfs:range</c> property — the range of a property.</summary>
        public static Utf8String Range { get; } = new(RangeBytes);

        /// <summary>The <c>rdfs:label</c> property — a human-readable name.</summary>
        public static Utf8String Label { get; } = new(LabelBytes);

        /// <summary>The <c>rdfs:comment</c> property — a human-readable description.</summary>
        public static Utf8String Comment { get; } = new(CommentBytes);

        /// <summary>The <c>rdfs:seeAlso</c> property — a pointer to additional information.</summary>
        public static Utf8String SeeAlso { get; } = new(SeeAlsoBytes);

        /// <summary>The <c>rdfs:isDefinedBy</c> property — the defining resource.</summary>
        public static Utf8String IsDefinedBy { get; } = new(IsDefinedByBytes);

        /// <summary>The <c>rdfs:member</c> property — a member of a container.</summary>
        public static Utf8String Member { get; } = new(MemberBytes);
    }
}
