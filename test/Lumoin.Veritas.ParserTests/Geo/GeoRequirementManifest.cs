using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// Loads the house-authored GeoSPARQL 1.1 requirement census manifest
/// (<c>Material/Geo/manifests/geosparql-11-requirement-census.ttl</c>) into
/// <see cref="GeoRequirementCase"/> entries, one per manifest node carrying an
/// <c>arm:requirement</c> property, ordered by requirement id. The manifest is trusted harness
/// infrastructure: a parse error fails loudly here, while roster and coherence defects are the coverage
/// ledger's to name (<see cref="GeoConformanceCoverageTests"/>).
/// </summary>
internal static class GeoRequirementManifest
{
    /// <summary>The manifest's file name under <c>Material/Geo/manifests/</c>.</summary>
    private const string ManifestFileName = "geosparql-11-requirement-census.ttl";

    /// <summary>Loads and orders the census entries from the source tree.</summary>
    /// <returns>The census entries ordered by requirement id.</returns>
    public static ImmutableArray<GeoRequirementCase> Load()
    {
        string path = W3cCorpusPath.For("Geo", "manifests", ManifestFileName);
        byte[] bytes = File.ReadAllBytes(path);
        Uri manifestUri = new(Path.GetFullPath(path));

        List<Quad> quads = [];
        DiagnosticBag diagnostics = new();
        foreach(Quad quad in TurtleReader.Read(bytes, TurtleSyntax.Turtle, diagnostics, pool: null, baseIri: manifestUri.AbsoluteUri))
        {
            quads.Add(quad);
        }

        if(diagnostics.HasErrors)
        {
            throw new InvalidOperationException($"Failed to parse the requirement census manifest '{path}': {TurtleConformanceReader.DescribeFirstError(diagnostics)}");
        }

        Dictionary<RdfTerm, List<Quad>> bySubject = [];
        foreach(Quad quad in quads)
        {
            if(!bySubject.TryGetValue(quad.Subject, out List<Quad>? owned))
            {
                owned = [];
                bySubject[quad.Subject] = owned;
            }

            owned.Add(quad);
        }

        List<GeoRequirementCase> cases = [];
        foreach(List<Quad> entry in bySubject.Values)
        {
            string requirementId = PropertyValue(entry, "urn:x-veritas:geosparql-arm#requirement"u8);
            if(requirementId.Length == 0)
            {
                continue;
            }

            cases.Add(new GeoRequirementCase(
                requirementId,
                PropertyValue(entry, "urn:x-veritas:geosparql-arm#conformanceClass"u8),
                PropertyValue(entry, "urn:x-veritas:geosparql-arm#bucket"u8),
                PropertyValue(entry, "urn:x-veritas:geosparql-arm#disposition"u8),
                PropertyValue(entry, "urn:x-veritas:geosparql-arm#reason"u8),
                PropertyValue(entry, "urn:x-veritas:geosparql-arm#evidence"u8)));
        }

        cases.Sort(CompareByRequirementId);

        return [.. cases];
    }

    /// <summary>The lexical value of an entry's literal property, or the empty string when absent.</summary>
    /// <param name="entry">The entry's quads.</param>
    /// <param name="predicateIri">The property's predicate IRI bytes.</param>
    /// <returns>The literal's lexical form, or empty.</returns>
    private static string PropertyValue(List<Quad> entry, ReadOnlySpan<byte> predicateIri)
    {
        foreach(Quad quad in entry)
        {
            if(quad.Predicate.Iri.Span.SequenceEqual(predicateIri) && quad.Object is Literal literal)
            {
                return literal.Value.ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>Orders two entries by requirement id, ordinal.</summary>
    /// <param name="first">The first entry.</param>
    /// <param name="second">The second entry.</param>
    /// <returns>The ordinal comparison of the requirement ids.</returns>
    private static int CompareByRequirementId(GeoRequirementCase first, GeoRequirementCase second)
    {
        return string.CompareOrdinal(first.RequirementId, second.RequirementId);
    }
}
