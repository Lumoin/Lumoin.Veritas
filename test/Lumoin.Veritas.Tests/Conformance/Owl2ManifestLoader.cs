using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.Tests.Conformance;

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
/// </remarks>
internal static class Owl2ManifestLoader
{
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string TestNamespace = "http://www.w3.org/2007/OWL/testOntology#";

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
        List<Quad> quads = [.. RdfXmlReader.Read(bytes, diagnostics, baseIri)];

        //The manifest is trusted infrastructure; a malformed one fails loudly.
        if(diagnostics.HasErrors)
        {
            throw new InvalidOperationException($"Failed to parse OWL 2 manifest '{manifestPath}'.");
        }

        Dictionary<string, List<Quad>> bySubject = IndexBySubject(quads);
        List<string> testSubjects = [];
        foreach(KeyValuePair<string, List<Quad>> entry in bySubject)
        {
            if(HasType(entry.Value, TestNamespace + "TestCase"))
            {
                testSubjects.Add(entry.Key);
            }
        }

        //Document order keeps runs and reports stable across loads.
        testSubjects.Sort(StringComparer.Ordinal);

        ImmutableArray<Owl2TestCase>.Builder builder = ImmutableArray.CreateBuilder<Owl2TestCase>(testSubjects.Count);
        foreach(string subject in testSubjects)
        {
            builder.Add(BuildTestCase(subject, bySubject[subject]));
        }

        return builder.ToImmutable();
    }

    private static Owl2TestCase BuildTestCase(string subjectKey, List<Quad> properties)
    {
        HashSet<string> kinds = [];
        HashSet<string> profiles = [];
        HashSet<string> species = [];
        HashSet<string> semantics = [];

        foreach(Quad quad in properties)
        {
            string predicate = quad.Predicate.Iri.ToString();

            if(predicate == RdfNamespace + "type" && quad.Object is NamedNode type)
            {
                string typeIri = type.Iri.ToString();
                if(typeIri.StartsWith(TestNamespace, StringComparison.Ordinal) && typeIri.EndsWith("Test", StringComparison.Ordinal))
                {
                    kinds.Add(typeIri[TestNamespace.Length..]);
                }
            }
            else if(predicate == TestNamespace + "profile" && quad.Object is NamedNode profile)
            {
                profiles.Add(LocalName(profile));
            }
            else if(predicate == TestNamespace + "species" && quad.Object is NamedNode speciesNode)
            {
                species.Add(LocalName(speciesNode));
            }
            else if(predicate == TestNamespace + "semantics" && quad.Object is NamedNode semanticsNode)
            {
                semantics.Add(LocalName(semanticsNode));
            }
        }

        string subjectIri = subjectKey.StartsWith("i:", StringComparison.Ordinal) ? subjectKey[2..] : subjectKey;
        Uri uri = Uri.TryCreate(subjectIri, UriKind.Absolute, out Uri? absolute) ? absolute : new Uri("urn:owl-test:" + subjectIri);

        return new Owl2TestCase(
            uri,
            Identifier: StringValue(properties, "identifier") ?? subjectIri,
            Description: StringValue(properties, "description") ?? string.Empty,
            Kinds: kinds,
            Profiles: profiles,
            Species: species,
            Semantics: semantics,
            RdfXmlPremise: StringValue(properties, "rdfXmlPremiseOntology"),
            RdfXmlConclusion: StringValue(properties, "rdfXmlConclusionOntology"),
            RdfXmlNonConclusion: StringValue(properties, "rdfXmlNonConclusionOntology"),
            RdfXmlInput: StringValue(properties, "rdfXmlInputOntology"),
            FunctionalPremise: StringValue(properties, "fsPremiseOntology"),
            FunctionalConclusion: StringValue(properties, "fsConclusionOntology"),
            FunctionalNonConclusion: StringValue(properties, "fsNonConclusionOntology"),
            HasImports: HasPredicate(properties, TestNamespace + "importedOntology"));
    }

    private static Dictionary<string, List<Quad>> IndexBySubject(List<Quad> quads)
    {
        Dictionary<string, List<Quad>> result = new(StringComparer.Ordinal);
        foreach(Quad quad in quads)
        {
            string key = quad.Subject switch
            {
                NamedNode named => "i:" + named.Iri.ToString(),
                BlankNode blank => "b:" + blank.Label.ToString(),
                _ => "x:" + quad.Subject.ToString()
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

    private static bool HasType(List<Quad> properties, string typeIri)
    {
        foreach(Quad quad in properties)
        {
            if(quad.Predicate.Iri.ToString() == RdfNamespace + "type"
                && quad.Object is NamedNode named
                && named.Iri.ToString() == typeIri)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPredicate(List<Quad> properties, string predicateIri)
    {
        foreach(Quad quad in properties)
        {
            if(quad.Predicate.Iri.ToString() == predicateIri)
            {
                return true;
            }
        }

        return false;
    }

    private static string? StringValue(List<Quad> properties, string localName)
    {
        string predicateIri = TestNamespace + localName;
        foreach(Quad quad in properties)
        {
            if(quad.Predicate.Iri.ToString() == predicateIri && quad.Object is Literal literal)
            {
                return literal.Value.ToString();
            }
        }

        return null;
    }

    private static string LocalName(NamedNode node)
    {
        string iri = node.Iri.ToString();

        return iri.StartsWith(TestNamespace, StringComparison.Ordinal) ? iri[TestNamespace.Length..] : iri;
    }
}
