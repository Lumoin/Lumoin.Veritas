using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.Unicode;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Parses a W3C OWL 2 test-ontology manifest (RDF/XML in the
/// <c>http://www.w3.org/2007/OWL/testOntology#</c> vocabulary) into
/// <see cref="Owl2TestCase"/> rows.
/// </summary>
/// <remarks>
/// <para>
/// The OWL 2 corpus differs from the RDF/SPARQL manifests in two ways the
/// loader leans on: each test subject is multi-typed with every test kind it
/// participates in, and the ontology documents under test are inline string
/// literals (<c>test:rdfXmlPremiseOntology</c> and friends), so there is no
/// sibling-file resolution at all.
/// </para>
/// <para>
/// The load path is byte-native: every predicate and type probe compares UTF-8
/// spans against the <see cref="ManifestVocabulary"/> literals, the subject
/// index keys on the parsed term's own <see cref="Utf8String"/>, and the inline
/// documents are handed on as the parsed literal's bytes. Only the short,
/// display-facing fields (identifier, description, and the kind, profile,
/// species, and semantics markers) materialize as managed strings.
/// </para>
/// <para>
/// The manifest is trusted infrastructure, so the loader's contract with the
/// corpus is enforced loudly rather than repaired silently: a test-case subject
/// is a named IRI, an inline document's bytes are well-formed UTF-8, and a
/// declared imported ontology resolves to a sibling node carrying an ontology
/// IRI. Each violation throws.
/// </para>
/// </remarks>
internal static class Owl2ManifestLoader
{
    /// <summary>
    /// The manifest vocabulary as UTF-8 literals: the single source of truth every
    /// predicate and type probe compares spans against, so no probe builds an IRI
    /// string at run time.
    /// </summary>
    private static class ManifestVocabulary
    {
        /// <summary>The <c>rdf:type</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> RdfType => "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8;

        /// <summary>The test-ontology namespace every manifest predicate and marker lives in.</summary>
        public static ReadOnlySpan<byte> TestNamespace => "http://www.w3.org/2007/OWL/testOntology#"u8;

        /// <summary>The <c>test:TestCase</c> type marker every test subject carries.</summary>
        public static ReadOnlySpan<byte> TestCaseType => "http://www.w3.org/2007/OWL/testOntology#TestCase"u8;

        /// <summary>The suffix a type marker's local name ends with to count as a test kind.</summary>
        public static ReadOnlySpan<byte> TestKindSuffix => "Test"u8;

        /// <summary>The <c>test:identifier</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> Identifier => "http://www.w3.org/2007/OWL/testOntology#identifier"u8;

        /// <summary>The <c>test:description</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> Description => "http://www.w3.org/2007/OWL/testOntology#description"u8;

        /// <summary>The <c>test:profile</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> Profile => "http://www.w3.org/2007/OWL/testOntology#profile"u8;

        /// <summary>The <c>test:species</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> Species => "http://www.w3.org/2007/OWL/testOntology#species"u8;

        /// <summary>The <c>test:semantics</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> Semantics => "http://www.w3.org/2007/OWL/testOntology#semantics"u8;

        /// <summary>The <c>test:rdfXmlPremiseOntology</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> RdfXmlPremiseOntology => "http://www.w3.org/2007/OWL/testOntology#rdfXmlPremiseOntology"u8;

        /// <summary>The <c>test:rdfXmlConclusionOntology</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> RdfXmlConclusionOntology => "http://www.w3.org/2007/OWL/testOntology#rdfXmlConclusionOntology"u8;

        /// <summary>The <c>test:rdfXmlNonConclusionOntology</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> RdfXmlNonConclusionOntology => "http://www.w3.org/2007/OWL/testOntology#rdfXmlNonConclusionOntology"u8;

        /// <summary>The <c>test:rdfXmlInputOntology</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> RdfXmlInputOntology => "http://www.w3.org/2007/OWL/testOntology#rdfXmlInputOntology"u8;

        /// <summary>The <c>test:fsPremiseOntology</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> FunctionalPremiseOntology => "http://www.w3.org/2007/OWL/testOntology#fsPremiseOntology"u8;

        /// <summary>The <c>test:fsConclusionOntology</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> FunctionalConclusionOntology => "http://www.w3.org/2007/OWL/testOntology#fsConclusionOntology"u8;

        /// <summary>The <c>test:fsNonConclusionOntology</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> FunctionalNonConclusionOntology => "http://www.w3.org/2007/OWL/testOntology#fsNonConclusionOntology"u8;

        /// <summary>The <c>test:fsInputOntology</c> predicate IRI.</summary>
        public static ReadOnlySpan<byte> FunctionalInputOntology => "http://www.w3.org/2007/OWL/testOntology#fsInputOntology"u8;

        /// <summary>The <c>test:importedOntology</c> predicate IRI, whose object names the sibling node carrying the imported document.</summary>
        public static ReadOnlySpan<byte> ImportedOntology => "http://www.w3.org/2007/OWL/testOntology#importedOntology"u8;

        /// <summary>The <c>test:importedOntologyIRI</c> predicate IRI, the ontology IRI an <c>owl:imports</c> reference resolves by.</summary>
        public static ReadOnlySpan<byte> ImportedOntologyIri => "http://www.w3.org/2007/OWL/testOntology#importedOntologyIRI"u8;
    }

    /// <summary>The kind of RDF term a manifest subject is; RDF/XML admits exactly these two.</summary>
    private enum SubjectKind
    {
        /// <summary>An IRI-identified subject; the key text is the IRI.</summary>
        Named,

        /// <summary>A blank-node subject; the key text is the label without the <c>_:</c> prefix.</summary>
        Blank
    }

    /// <summary>
    /// The subject index's key: the subject's kind together with the parsed term's own
    /// <see cref="Utf8String"/>, so indexing and lookup copy no bytes and build no strings.
    /// </summary>
    /// <param name="Kind">Whether the subject is a named node or a blank node.</param>
    /// <param name="Text">The subject's IRI or blank-node label, as the parsed term carries it.</param>
    private readonly record struct SubjectKey(SubjectKind Kind, Utf8String Text);

    /// <summary>
    /// Loads every test case declared in the manifest file.
    /// </summary>
    /// <param name="manifestPath">Absolute path to the manifest <c>.rdf</c> file.</param>
    /// <returns>The declared test cases, in document order.</returns>
    public static ImmutableArray<Owl2TestCase> Load(string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);

        byte[] bytes = File.ReadAllBytes(manifestPath);
        string baseIri = new Uri(Path.GetFullPath(manifestPath)).AbsoluteUri;
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(baseIri))];

        //The manifest is trusted infrastructure; a malformed one fails loudly.
        if(diagnostics.HasErrors)
        {
            throw new InvalidOperationException($"Failed to parse OWL 2 manifest '{manifestPath}'.");
        }

        Dictionary<SubjectKey, List<Quad>> bySubject = IndexBySubject(quads);
        List<SubjectKey> testSubjects = [];
        foreach(KeyValuePair<SubjectKey, List<Quad>> entry in bySubject)
        {
            if(!HasType(entry.Value, ManifestVocabulary.TestCaseType))
            {
                continue;
            }

            //A test-case subject is a named IRI: that IRI is the case's identity, its
            //Uri, and the text the row order sorts on. A blank-node subject carries no
            //IRI to stand for any of the three, so it fails loudly.
            if(entry.Key.Kind is not SubjectKind.Named)
            {
                throw new InvalidOperationException($"The manifest types blank node '{entry.Key.Text}' as test:TestCase; a test-case subject is a named IRI.");
            }

            testSubjects.Add(entry.Key);
        }

        //Document order keeps runs and reports stable across loads.
        testSubjects.Sort(CompareSubjectKeys);

        ImmutableArray<Owl2TestCase>.Builder builder = ImmutableArray.CreateBuilder<Owl2TestCase>(testSubjects.Count);
        foreach(SubjectKey subject in testSubjects)
        {
            builder.Add(BuildTestCase(subject, bySubject[subject], bySubject));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Orders subject keys by kind, then byte-lexicographically over the subject text.
    /// The resulting order is the manifest row order every consumer reads and every
    /// corpus fold hashes in.
    /// </summary>
    /// <param name="first">The first key.</param>
    /// <param name="second">The second key.</param>
    /// <returns>A negative value when <paramref name="first"/> sorts earlier, a positive value when <paramref name="second"/> does, zero when the keys are equal.</returns>
    private static int CompareSubjectKeys(SubjectKey first, SubjectKey second)
    {
        int byKind = ((int)first.Kind).CompareTo((int)second.Kind);

        return byKind != 0 ? byKind : first.Text.Span.SequenceCompareTo(second.Text.Span);
    }

    /// <summary>Builds one test case from its subject's quads, resolving the imported-ontology siblings through the subject index.</summary>
    /// <param name="subject">The test subject's index key.</param>
    /// <param name="properties">The subject's quads.</param>
    /// <param name="bySubject">The whole manifest's subject index, carrying the imported-ontology sibling nodes.</param>
    /// <returns>The built test case.</returns>
    private static Owl2TestCase BuildTestCase(SubjectKey subject, List<Quad> properties, Dictionary<SubjectKey, List<Quad>> bySubject)
    {
        HashSet<string> kinds = [];
        HashSet<string> profiles = [];
        HashSet<string> species = [];
        HashSet<string> semantics = [];

        foreach(Quad quad in properties)
        {
            ReadOnlySpan<byte> predicate = quad.Predicate.Iri.Span;

            if(predicate.SequenceEqual(ManifestVocabulary.RdfType) && quad.Object is NamedNode type)
            {
                ReadOnlySpan<byte> typeIri = type.Iri.Span;
                if(typeIri.StartsWith(ManifestVocabulary.TestNamespace) && typeIri.EndsWith(ManifestVocabulary.TestKindSuffix))
                {
                    kinds.Add(Encoding.UTF8.GetString(typeIri[ManifestVocabulary.TestNamespace.Length..]));
                }
            }
            else if(predicate.SequenceEqual(ManifestVocabulary.Profile) && quad.Object is NamedNode profile)
            {
                profiles.Add(LocalName(profile));
            }
            else if(predicate.SequenceEqual(ManifestVocabulary.Species) && quad.Object is NamedNode speciesNode)
            {
                species.Add(LocalName(speciesNode));
            }
            else if(predicate.SequenceEqual(ManifestVocabulary.Semantics) && quad.Object is NamedNode semanticsNode)
            {
                semantics.Add(LocalName(semanticsNode));
            }
        }

        string subjectIri = subject.Text.ToString();
        Uri uri = Uri.TryCreate(subjectIri, UriKind.Absolute, out Uri? absolute) ? absolute : new Uri("urn:owl-test:" + subjectIri);

        return new Owl2TestCase(
            uri,
            Identifier: TextValue(properties, ManifestVocabulary.Identifier) ?? subjectIri,
            Description: TextValue(properties, ManifestVocabulary.Description) ?? string.Empty,
            Kinds: kinds,
            Profiles: profiles,
            Species: species,
            Semantics: semantics,
            RdfXmlPremise: DocumentValue(properties, ManifestVocabulary.RdfXmlPremiseOntology),
            RdfXmlConclusion: DocumentValue(properties, ManifestVocabulary.RdfXmlConclusionOntology),
            RdfXmlNonConclusion: DocumentValue(properties, ManifestVocabulary.RdfXmlNonConclusionOntology),
            RdfXmlInput: DocumentValue(properties, ManifestVocabulary.RdfXmlInputOntology),
            FunctionalPremise: TextValue(properties, ManifestVocabulary.FunctionalPremiseOntology),
            FunctionalConclusion: TextValue(properties, ManifestVocabulary.FunctionalConclusionOntology),
            FunctionalNonConclusion: TextValue(properties, ManifestVocabulary.FunctionalNonConclusionOntology),
            Imports: BuildImports(properties, bySubject));
    }

    /// <summary>Collects the imported ontologies a test case supplies, resolving each reference to its sibling node in the manifest.</summary>
    /// <param name="properties">The test subject's quads.</param>
    /// <param name="bySubject">The whole manifest's subject index.</param>
    /// <returns>The supplied imported ontologies, in declaration order.</returns>
    /// <exception cref="InvalidOperationException">A referenced sibling node is absent or declares no ontology IRI.</exception>
    private static List<Owl2ImportedOntology> BuildImports(List<Quad> properties, Dictionary<SubjectKey, List<Quad>> bySubject)
    {
        List<Owl2ImportedOntology> imports = [];
        foreach(Quad quad in properties)
        {
            if(!quad.Predicate.Iri.Span.SequenceEqual(ManifestVocabulary.ImportedOntology) || quad.Object is not NamedNode reference)
            {
                continue;
            }

            //The referenced node is a sibling in the same manifest carrying
            //the resolvable ontology IRI and the inline document; the
            //manifest is trusted infrastructure, so a dangling reference
            //fails loudly.
            if(!bySubject.TryGetValue(new SubjectKey(SubjectKind.Named, reference.Iri), out List<Quad>? importedProperties))
            {
                throw new InvalidOperationException($"The manifest declares imported ontology '{reference.Iri}' but carries no node for it.");
            }

            if(ResourceValue(importedProperties, ManifestVocabulary.ImportedOntologyIri) is not { } iri)
            {
                throw new InvalidOperationException($"Imported ontology node '{reference.Iri}' declares no test:importedOntologyIRI.");
            }

            //The ontology IRI and the inline document together are the value identity of
            //the import parse cache's key, so the IRI is wrapped eagerly hashed over the
            //parsed term's own memory: the cache's probes pay its content hash once per
            //load rather than once per probe.
            imports.Add(new Owl2ImportedOntology(
                new Utf8String(iri.Memory),
                RdfXml: DocumentValue(importedProperties, ManifestVocabulary.RdfXmlInputOntology),
                Functional: TextValue(importedProperties, ManifestVocabulary.FunctionalInputOntology)));
        }

        return imports;
    }

    /// <summary>Groups the manifest's quads by subject, keying on the parsed term's own UTF-8 text.</summary>
    /// <param name="quads">The parsed manifest quads.</param>
    /// <returns>The subject index.</returns>
    /// <exception cref="InvalidOperationException">A quad's subject is neither a named node nor a blank node.</exception>
    private static Dictionary<SubjectKey, List<Quad>> IndexBySubject(List<Quad> quads)
    {
        Dictionary<SubjectKey, List<Quad>> result = [];
        foreach(Quad quad in quads)
        {
            //RDF/XML subjects are IRIs or blank nodes; the manifest is trusted
            //infrastructure, so any other subject term fails loudly.
            SubjectKey key = quad.Subject switch
            {
                NamedNode named => new SubjectKey(SubjectKind.Named, named.Iri),
                BlankNode blank => new SubjectKey(SubjectKind.Blank, blank.Label),
                _ => throw new InvalidOperationException("The manifest carries a quad whose subject is neither a named node nor a blank node.")
            };

            if(!result.TryGetValue(key, out List<Quad>? bucket))
            {
                bucket = [];
                result[key] = bucket;
            }

            bucket.Add(quad);
        }

        return result;
    }

    /// <summary>Reports whether a subject carries the given <c>rdf:type</c> marker.</summary>
    /// <param name="properties">The subject's quads.</param>
    /// <param name="typeIri">The type marker's IRI as UTF-8 bytes.</param>
    /// <returns><see langword="true"/> when the subject is typed with the marker.</returns>
    private static bool HasType(List<Quad> properties, ReadOnlySpan<byte> typeIri)
    {
        foreach(Quad quad in properties)
        {
            if(quad.Predicate.Iri.Span.SequenceEqual(ManifestVocabulary.RdfType)
                && quad.Object is NamedNode named
                && named.Iri.Span.SequenceEqual(typeIri))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads a subject's first named-node object for a predicate, as the parsed term's own UTF-8 IRI.</summary>
    /// <param name="properties">The subject's quads.</param>
    /// <param name="predicateIri">The predicate's IRI as UTF-8 bytes.</param>
    /// <returns>The object IRI, or <see langword="null"/> when the subject declares none.</returns>
    private static Utf8String? ResourceValue(List<Quad> properties, ReadOnlySpan<byte> predicateIri)
    {
        foreach(Quad quad in properties)
        {
            if(quad.Predicate.Iri.Span.SequenceEqual(predicateIri) && quad.Object is NamedNode named)
            {
                return named.Iri;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a subject's first literal object for a predicate as the parsed literal's own
    /// UTF-8 bytes. This is the inline-document path: the document text is wrapped
    /// zero-copy over the literal's memory and never materialized as a managed string.
    /// The wrap is eagerly hashed, so the keyed probes downstream — the census premise
    /// deduplication set and the import parse cache's record key — pay the document's
    /// content hash once per load rather than once per probe. A document whose bytes are
    /// not well-formed UTF-8 fails loudly rather than folding to replacement characters,
    /// and the refusal precedes the wrap so an ill-formed document is never hashed.
    /// </summary>
    /// <param name="properties">The subject's quads.</param>
    /// <param name="predicateIri">The predicate's IRI as UTF-8 bytes.</param>
    /// <returns>The literal's bytes, or <see langword="null"/> when the subject declares none.</returns>
    /// <exception cref="InvalidOperationException">The literal's bytes are not well-formed UTF-8.</exception>
    private static Utf8String? DocumentValue(List<Quad> properties, ReadOnlySpan<byte> predicateIri)
    {
        foreach(Quad quad in properties)
        {
            if(quad.Predicate.Iri.Span.SequenceEqual(predicateIri) && quad.Object is Literal literal)
            {
                if(!Utf8.IsValid(literal.Value.Span))
                {
                    throw new InvalidOperationException($"The manifest carries an inline document under '{quad.Predicate.Iri}' whose bytes are not well-formed UTF-8.");
                }

                return new Utf8String(literal.Value.Memory);
            }
        }

        return null;
    }

    /// <summary>Reads a subject's first literal object for a predicate as a managed string; reserved for the short, display-facing fields.</summary>
    /// <param name="properties">The subject's quads.</param>
    /// <param name="predicateIri">The predicate's IRI as UTF-8 bytes.</param>
    /// <returns>The literal's text, or <see langword="null"/> when the subject declares none.</returns>
    private static string? TextValue(List<Quad> properties, ReadOnlySpan<byte> predicateIri)
    {
        foreach(Quad quad in properties)
        {
            if(quad.Predicate.Iri.Span.SequenceEqual(predicateIri) && quad.Object is Literal literal)
            {
                return literal.Value.ToString();
            }
        }

        return null;
    }

    /// <summary>Renders a marker node's local name: the part after the test namespace, or the whole IRI for a marker outside it.</summary>
    /// <param name="node">The marker node.</param>
    /// <returns>The local name.</returns>
    private static string LocalName(NamedNode node)
    {
        ReadOnlySpan<byte> iri = node.Iri.Span;
        ReadOnlySpan<byte> localName = iri.StartsWith(ManifestVocabulary.TestNamespace) ? iri[ManifestVocabulary.TestNamespace.Length..] : iri;

        return Encoding.UTF8.GetString(localName);
    }
}
