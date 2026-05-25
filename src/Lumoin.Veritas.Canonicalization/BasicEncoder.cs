using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// Implements RDF 1.2 basic-encoding and its inverse per
/// <see href="https://www.w3.org/TR/rdf12-interop/#basic-encoding">RDF 1.2 Interoperability §3</see>.
/// </summary>
/// <remarks>
/// <para>
/// Basic-encoding rewrites an RDF 1.2 Full dataset — one that may contain triple terms
/// (<c>&lt;&lt;( s p o )&gt;&gt;</c>) — into an equivalent Basic dataset that contains none. Each triple
/// term is replaced by a fresh blank node carrying four assertions: <c>rdf:type rdf:PropositionForm</c>
/// and one each of <c>rdf:propositionFormSubject</c> / <c>rdf:propositionFormPredicate</c> /
/// <c>rdf:propositionFormObject</c>. Identical triple terms within a graph collapse to one blank node, so
/// all occurrences of a triple term are replaced consistently. Decoding reverses the transformation.
/// </para>
/// <para>
/// This is the spec's resolution for canonicalizing datasets with triple terms (basic-encode, then run
/// RDFC-1.0). It is offered as a standalone, composable operation; <see cref="RdfCanonicalizer"/> does
/// not apply it by default, because no published conformance corpus pins the encoded canonical output.
/// </para>
/// </remarks>
public static class BasicEncoder
{
    private static NamedNode TypeTerm { get; } = new(Vocabulary.Rdf.Type);

    private static NamedNode PropositionFormTerm { get; } = new(Vocabulary.Rdf.PropositionForm);

    private static NamedNode PropositionFormSubjectTerm { get; } = new(Vocabulary.Rdf.PropositionFormSubject);

    private static NamedNode PropositionFormPredicateTerm { get; } = new(Vocabulary.Rdf.PropositionFormPredicate);

    private static NamedNode PropositionFormObjectTerm { get; } = new(Vocabulary.Rdf.PropositionFormObject);

    /// <summary>
    /// Basic-encodes a Full dataset, replacing every triple term with a blank node and its four
    /// <c>rdf:PropositionForm</c> assertions. A dataset with no triple term is returned unchanged.
    /// </summary>
    /// <param name="quads">The Full dataset to encode.</param>
    /// <param name="pool">The pool that interns the labels of the minted blank nodes.</param>
    /// <returns>The basic-encoded equivalent dataset.</returns>
    public static List<Quad> Encode(IEnumerable<Quad> quads, Utf8StringPool pool)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(pool);

        List<Quad> dataset = Materialize(quads);

        //Minted blank-node labels must not collide with blank nodes already present anywhere in the
        //input, including blank nodes nested inside triple terms.
        HashSet<string> usedLabels = [];
        foreach(Quad quad in dataset)
        {
            CollectBlankLabels(quad.Subject, usedLabels);
            CollectBlankLabels(quad.Object, usedLabels);
            CollectBlankLabels(quad.Graph, usedLabels);
        }

        Dictionary<(RdfTerm? Graph, TripleTerm Term), BlankNode> encoded = [];
        List<Quad> output = new(dataset.Count);
        int counter = 0;

        foreach(Quad quad in dataset)
        {
            RdfTerm subject = EncodeTerm(quad.Subject, quad.Graph, output, encoded, usedLabels, ref counter, pool);
            RdfTerm objectTerm = EncodeTerm(quad.Object, quad.Graph, output, encoded, usedLabels, ref counter, pool);

            output.Add(new Quad(subject, quad.Predicate, objectTerm, quad.Graph));
        }

        return output;
    }

    /// <summary>
    /// Basic-decodes a dataset, reconstructing each <c>rdf:PropositionForm</c> marker blank node into the
    /// triple term it stands for and substituting it back into the dataset. A dataset with no marker is
    /// returned unchanged.
    /// </summary>
    /// <param name="quads">The Basic dataset to decode.</param>
    /// <returns>The Full equivalent dataset, with triple terms restored.</returns>
    /// <exception cref="BasicEncodingException">
    /// The marker structure is malformed: a marker is missing or duplicates one of its
    /// subject/predicate/object assertions, the predicate position is not an IRI, the markers form a
    /// cycle, or the input mixes a triple term with a marker.
    /// </exception>
    public static List<Quad> Decode(IEnumerable<Quad> quads)
    {
        ArgumentNullException.ThrowIfNull(quads);

        List<Quad> dataset = Materialize(quads);

        HashSet<string> markerLabels = [];
        bool hasTripleTerm = false;
        foreach(Quad quad in dataset)
        {
            if(ContainsTripleTerm(quad.Subject) || ContainsTripleTerm(quad.Object) || ContainsTripleTerm(quad.Graph))
            {
                hasTripleTerm = true;
            }

            if(quad.Subject is BlankNode marker
                && quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type)
                && quad.Object is NamedNode typeObject
                && typeObject.Iri.Equals(Vocabulary.Rdf.PropositionForm))
            {
                markerLabels.Add(marker.Label.ToString());
            }
        }

        if(hasTripleTerm && markerLabels.Count > 0)
        {
            throw new BasicEncodingException(
                "Input mixes a triple term with an rdf:PropositionForm marker; the basic-encoded form is ambiguous.");
        }

        if(markerLabels.Count == 0)
        {
            return dataset;
        }

        Dictionary<string, RdfTerm> markerSubjects = [];
        Dictionary<string, NamedNode> markerPredicates = [];
        Dictionary<string, RdfTerm> markerObjects = [];
        CollectMarkerParts(dataset, markerLabels, markerSubjects, markerPredicates, markerObjects);

        foreach(string label in markerLabels)
        {
            if(!markerSubjects.ContainsKey(label) || !markerPredicates.ContainsKey(label) || !markerObjects.ContainsKey(label))
            {
                throw new BasicEncodingException(
                    string.Create(CultureInfo.InvariantCulture, $"rdf:PropositionForm marker '_:{label}' is missing a subject, predicate, or object assertion."));
            }
        }

        Dictionary<string, TripleTerm> reconstructed = [];
        foreach(string label in markerLabels)
        {
            ReconstructMarker(label, markerLabels, markerSubjects, markerPredicates, markerObjects, reconstructed);
        }

        List<Quad> output = [];
        foreach(Quad quad in dataset)
        {
            if(IsMarkerDefiningQuad(quad, markerLabels))
            {
                continue;
            }

            RdfTerm subject = Substitute(quad.Subject, reconstructed);
            RdfTerm objectTerm = Substitute(quad.Object, reconstructed);
            RdfTerm? graph = quad.Graph is null ? null : Substitute(quad.Graph, reconstructed);

            output.Add(new Quad(subject, quad.Predicate, objectTerm, graph));
        }

        return output;
    }

    private static RdfTerm EncodeTerm(
        RdfTerm term,
        RdfTerm? graph,
        List<Quad> output,
        Dictionary<(RdfTerm? Graph, TripleTerm Term), BlankNode> encoded,
        HashSet<string> usedLabels,
        ref int counter,
        Utf8StringPool pool)
    {
        if(term is not TripleTerm root)
        {
            return term;
        }

        //Encode the triple-term tree bottom-up with an explicit stack so a nested triple term is
        //replaced by its blank node before the enclosing term references it. The expanded flag marks a
        //frame whose nested triple terms have been queued, mirroring a post-order traversal.
        Stack<EncodeFrame> stack = new();
        stack.Push(new EncodeFrame(root, Expanded: false));

        while(stack.Count > 0)
        {
            EncodeFrame frame = stack.Pop();
            TripleTerm current = frame.Term;

            if(encoded.ContainsKey((graph, current)))
            {
                continue;
            }

            if(!frame.Expanded)
            {
                stack.Push(new EncodeFrame(current, Expanded: true));

                if(current.Subject is TripleTerm nestedSubject && !encoded.ContainsKey((graph, nestedSubject)))
                {
                    stack.Push(new EncodeFrame(nestedSubject, Expanded: false));
                }

                if(current.Object is TripleTerm nestedObject && !encoded.ContainsKey((graph, nestedObject)))
                {
                    stack.Push(new EncodeFrame(nestedObject, Expanded: false));
                }

                continue;
            }

            RdfTerm encodedSubject = current.Subject is TripleTerm subjectTerm
                ? encoded[(graph, subjectTerm)]
                : current.Subject;
            RdfTerm encodedObject = current.Object is TripleTerm objectTerm
                ? encoded[(graph, objectTerm)]
                : current.Object;

            BlankNode blank = MintBlankNode(usedLabels, ref counter, pool);
            output.Add(new Quad(blank, TypeTerm, PropositionFormTerm, graph));
            output.Add(new Quad(blank, PropositionFormSubjectTerm, encodedSubject, graph));
            output.Add(new Quad(blank, PropositionFormPredicateTerm, current.Predicate, graph));
            output.Add(new Quad(blank, PropositionFormObjectTerm, encodedObject, graph));

            encoded[(graph, current)] = blank;
        }

        return encoded[(graph, root)];
    }

    private static BlankNode MintBlankNode(HashSet<string> usedLabels, ref int counter, Utf8StringPool pool)
    {
        string label;
        do
        {
            label = string.Create(CultureInfo.InvariantCulture, $"e{counter}");
            counter++;
        }
        while(usedLabels.Contains(label));

        usedLabels.Add(label);

        return new BlankNode(pool.Intern(label));
    }

    private static void CollectMarkerParts(
        List<Quad> dataset,
        HashSet<string> markerLabels,
        Dictionary<string, RdfTerm> markerSubjects,
        Dictionary<string, NamedNode> markerPredicates,
        Dictionary<string, RdfTerm> markerObjects)
    {
        foreach(Quad quad in dataset)
        {
            if(quad.Subject is not BlankNode blank)
            {
                continue;
            }

            string label = blank.Label.ToString();
            if(!markerLabels.Contains(label))
            {
                continue;
            }

            if(quad.Predicate.Iri.Equals(Vocabulary.Rdf.PropositionFormSubject))
            {
                AddMarkerPart(markerSubjects, label, quad.Object, "rdf:propositionFormSubject");
            }
            else if(quad.Predicate.Iri.Equals(Vocabulary.Rdf.PropositionFormPredicate))
            {
                if(quad.Object is not NamedNode predicate)
                {
                    throw new BasicEncodingException(
                        string.Create(CultureInfo.InvariantCulture, $"rdf:PropositionForm marker '_:{label}' has a predicate that is not an IRI."));
                }

                if(markerPredicates.ContainsKey(label))
                {
                    throw DuplicatePart(label, "rdf:propositionFormPredicate");
                }

                markerPredicates[label] = predicate;
            }
            else if(quad.Predicate.Iri.Equals(Vocabulary.Rdf.PropositionFormObject))
            {
                AddMarkerPart(markerObjects, label, quad.Object, "rdf:propositionFormObject");
            }
        }
    }

    private static void AddMarkerPart(Dictionary<string, RdfTerm> parts, string label, RdfTerm value, string predicateName)
    {
        if(parts.ContainsKey(label))
        {
            throw DuplicatePart(label, predicateName);
        }

        parts[label] = value;
    }

    private static BasicEncodingException DuplicatePart(string label, string predicateName)
    {
        return new BasicEncodingException(
            string.Create(CultureInfo.InvariantCulture, $"rdf:PropositionForm marker '_:{label}' has more than one {predicateName} assertion."));
    }

    private static void ReconstructMarker(
        string root,
        HashSet<string> markerLabels,
        Dictionary<string, RdfTerm> markerSubjects,
        Dictionary<string, NamedNode> markerPredicates,
        Dictionary<string, RdfTerm> markerObjects,
        Dictionary<string, TripleTerm> reconstructed)
    {
        if(reconstructed.ContainsKey(root))
        {
            return;
        }

        //Reconstruct bottom-up: a marker whose subject or object references another marker is built only
        //after that marker. The in-progress set detects a reference cycle, which has no Full equivalent.
        Stack<DecodeFrame> stack = new();
        HashSet<string> inProgress = [];
        stack.Push(new DecodeFrame(root, Expanded: false));

        while(stack.Count > 0)
        {
            DecodeFrame frame = stack.Pop();
            string label = frame.Label;

            if(reconstructed.ContainsKey(label))
            {
                continue;
            }

            if(!frame.Expanded)
            {
                inProgress.Add(label);
                stack.Push(new DecodeFrame(label, Expanded: true));
                PushMarkerDependency(markerSubjects[label], markerLabels, reconstructed, inProgress, stack);
                PushMarkerDependency(markerObjects[label], markerLabels, reconstructed, inProgress, stack);

                continue;
            }

            inProgress.Remove(label);
            RdfTerm subject = ResolveMarkerValue(markerSubjects[label], markerLabels, reconstructed);
            RdfTerm objectTerm = ResolveMarkerValue(markerObjects[label], markerLabels, reconstructed);

            reconstructed[label] = new TripleTerm(subject, markerPredicates[label], objectTerm);
        }
    }

    private static void PushMarkerDependency(
        RdfTerm value,
        HashSet<string> markerLabels,
        Dictionary<string, TripleTerm> reconstructed,
        HashSet<string> inProgress,
        Stack<DecodeFrame> stack)
    {
        if(value is not BlankNode blank)
        {
            return;
        }

        string label = blank.Label.ToString();
        if(!markerLabels.Contains(label) || reconstructed.ContainsKey(label))
        {
            return;
        }

        if(inProgress.Contains(label))
        {
            throw new BasicEncodingException("rdf:PropositionForm markers form a reference cycle, which has no triple-term equivalent.");
        }

        stack.Push(new DecodeFrame(label, Expanded: false));
    }

    private static RdfTerm ResolveMarkerValue(RdfTerm value, HashSet<string> markerLabels, Dictionary<string, TripleTerm> reconstructed)
    {
        if(value is BlankNode blank && markerLabels.Contains(blank.Label.ToString()))
        {
            return reconstructed[blank.Label.ToString()];
        }

        return value;
    }

    private static RdfTerm Substitute(RdfTerm term, Dictionary<string, TripleTerm> reconstructed)
    {
        if(term is BlankNode blank && reconstructed.TryGetValue(blank.Label.ToString(), out TripleTerm? tripleTerm))
        {
            return tripleTerm;
        }

        return term;
    }

    private static bool IsMarkerDefiningQuad(Quad quad, HashSet<string> markerLabels)
    {
        if(quad.Subject is not BlankNode blank || !markerLabels.Contains(blank.Label.ToString()))
        {
            return false;
        }

        if(quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type))
        {
            return quad.Object is NamedNode typeObject && typeObject.Iri.Equals(Vocabulary.Rdf.PropositionForm);
        }

        return quad.Predicate.Iri.Equals(Vocabulary.Rdf.PropositionFormSubject)
            || quad.Predicate.Iri.Equals(Vocabulary.Rdf.PropositionFormPredicate)
            || quad.Predicate.Iri.Equals(Vocabulary.Rdf.PropositionFormObject);
    }

    private static void CollectBlankLabels(RdfTerm? term, ICollection<string> sink)
    {
        switch(term)
        {
            case null or NamedNode or Literal:
            {
                return;
            }

            case BlankNode topBlank:
            {
                sink.Add(topBlank.Label.ToString());

                return;
            }

            default:
            {
                break;
            }
        }

        Stack<RdfTerm> pending = new();
        pending.Push(term);

        while(pending.Count > 0)
        {
            RdfTerm current = pending.Pop();
            switch(current)
            {
                case BlankNode blank:
                {
                    sink.Add(blank.Label.ToString());

                    break;
                }

                case TripleTerm tripleTerm:
                {
                    pending.Push(tripleTerm.Subject);
                    pending.Push(tripleTerm.Object);

                    break;
                }

                default:
                {
                    break;
                }
            }
        }
    }

    private static bool ContainsTripleTerm(RdfTerm? term)
    {
        return term is TripleTerm;
    }

    private static List<Quad> Materialize(IEnumerable<Quad> quads)
    {
        List<Quad> dataset = quads is ICollection<Quad> collection ? new List<Quad>(collection.Count) : [];
        foreach(Quad quad in quads)
        {
            dataset.Add(quad);
        }

        return dataset;
    }

    [DebuggerDisplay("Encode {Term} expanded={Expanded}")]
    private readonly record struct EncodeFrame(TripleTerm Term, bool Expanded);

    [DebuggerDisplay("Decode {Label,nq} expanded={Expanded}")]
    private readonly record struct DecodeFrame(string Label, bool Expanded);
}
