using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Parses SHACL <c>sh:path</c> objects into <see cref="PropertyPath"/>
/// trees. Called once per property shape during phase 2 of the loader.
/// </summary>
/// <remarks>
/// <para>
/// <b>Syntactic forms.</b> Per SHACL 1.2 Core §2.3, a path value is one
/// of:
/// </para>
/// <list type="bullet">
///   <item><description><b>IRI</b> — a predicate path.</description></item>
///   <item><description><b>RDF list</b> — a sequence path; each member is itself a path.</description></item>
///   <item><description>
///   <b>Blank node with <c>sh:inversePath</c></b> — an inverse path.
///   </description></item>
///   <item><description>
///   <b>Blank node with <c>sh:alternativePath</c></b> — an alternative path
///   over the list value of that predicate.
///   </description></item>
///   <item><description>
///   <b>Blank node with <c>sh:zeroOrMorePath</c></b>/<c>sh:oneOrMorePath</c>/<c>sh:zeroOrOnePath</c>
///   — the corresponding iteration.
///   </description></item>
/// </list>
/// <para>
/// <b>Dispatching.</b> Because the RDF syntax for paths is inherently
/// ambiguous — every intermediate path is either a named predicate IRI or
/// a blank node whose outgoing triples determine the operator — the
/// parser pivots on <see cref="RdfTerm"/> kind and blank-node outgoing
/// structure. IRI terms resolve directly to
/// <see cref="PredicatePath"/>. Blank-node terms are queried for their
/// single discriminating operator predicate.
/// </para>
/// <para>
/// <b>Well-formedness.</b> SHACL §2.3 forbids shared blank nodes across
/// path structures and cycles within paths. This parser follows the
/// spec's assumption and does not detect such malformed graphs; if the
/// shape graph contains a cycle in <c>rdf:rest</c> chains or shared path
/// blank nodes, the parser's walk will recurse indefinitely. Detecting
/// this is the job of shape-graph validators, not of the loader.
/// </para>
/// <para>
/// <b>Iteration.</b> Uses an explicit recursion via C# method calls on
/// the stack rather than a <see cref="Stack{T}"/>; path structures are
/// shallow (depth rarely exceeds single digits) and the code is clearer.
/// If deeper paths ever appear in practice, this can be flattened.
/// </para>
/// </remarks>
internal static class ShapePathParser
{
    /// <summary>
    /// Parses the path object <paramref name="pathTerm"/>, walking its
    /// substructure through <paramref name="shapeGraphMatch"/>.
    /// </summary>
    /// <param name="pathTerm">The term referenced by <c>sh:path</c>.</param>
    /// <param name="shapeGraphMatch">Triple-match delegate over the shape graph.</param>
    /// <param name="dictionary">Term dictionary for classifying path terms as IRI vs blank node.</param>
    /// <param name="pathVocabulary">Pre-resolved path-operator predicate ids.</param>
    /// <param name="rdfListIds">Pre-resolved <c>rdf:first/rest/nil</c> ids for list walking.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The parsed <see cref="PropertyPath"/>.</returns>
    /// <exception cref="System.FormatException">
    /// The path structure is malformed — for example a blank node with no
    /// path-operator predicate, an RDF list that terminates without
    /// <c>rdf:nil</c>, or a literal in path position.
    /// </exception>
    public static async Task<PropertyPath> ParseAsync(
        TermId pathTerm,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        PathVocabularyIds pathVocabulary,
        RdfListIds rdfListIds,
        CancellationToken cancellationToken = default)
    {
        //Classify the term.
        RdfTerm term = dictionary.Resolve(pathTerm);
        if(term is NamedNode)
        {
            //IRI — predicate path.
            return new PredicatePath(IriId.FromUnchecked(pathTerm));
        }

        if(term is not BlankNode)
        {
            throw new System.FormatException(
                $"sh:path value must be an IRI or blank node; got {term}.");
        }

        //Blank-node term. Look for the discriminating operator predicate.
        //An RDF list (sequence) is distinguished from operator-blank-node
        //forms by carrying rdf:first. Operator forms carry exactly one of
        //the sh:path-operator predicates.
        List<EncodedTriple> outgoing = new();
        await foreach(EncodedTriple triple in shapeGraphMatch(pathTerm, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            outgoing.Add(triple);
        }

        //rdf:first present → this is a list head → sequence path.
        foreach(EncodedTriple triple in outgoing)
        {
            if(triple.Predicate == rdfListIds.RdfFirst)
            {
                return await ParseSequencePathAsync(
                    pathTerm, shapeGraphMatch, dictionary, pathVocabulary, rdfListIds, cancellationToken).ConfigureAwait(false);
            }
        }

        //Operator blank node: exactly one path-operator predicate.
        foreach(EncodedTriple triple in outgoing)
        {
            TermId innerTerm = triple.Object;

            if(triple.Predicate == pathVocabulary.InversePath)
            {
                PropertyPath inner = await ParseAsync(
                    innerTerm, shapeGraphMatch, dictionary, pathVocabulary, rdfListIds, cancellationToken).ConfigureAwait(false);
                return new InversePath(inner);
            }

            if(triple.Predicate == pathVocabulary.AlternativePath)
            {
                //sh:alternativePath is an RDF list of alternatives.
                ImmutableArray<PropertyPath> alternatives = await ParsePathListAsync(
                    innerTerm, shapeGraphMatch, dictionary, pathVocabulary, rdfListIds, cancellationToken).ConfigureAwait(false);
                if(alternatives.Length < 2)
                {
                    throw new System.FormatException(
                        "sh:alternativePath must have at least two alternatives.");
                }

                return new AlternativePath(alternatives);
            }

            if(triple.Predicate == pathVocabulary.ZeroOrMorePath)
            {
                PropertyPath inner = await ParseAsync(
                    innerTerm, shapeGraphMatch, dictionary, pathVocabulary, rdfListIds, cancellationToken).ConfigureAwait(false);
                return new ZeroOrMorePath(inner);
            }

            if(triple.Predicate == pathVocabulary.OneOrMorePath)
            {
                PropertyPath inner = await ParseAsync(
                    innerTerm, shapeGraphMatch, dictionary, pathVocabulary, rdfListIds, cancellationToken).ConfigureAwait(false);
                return new OneOrMorePath(inner);
            }

            if(triple.Predicate == pathVocabulary.ZeroOrOnePath)
            {
                PropertyPath inner = await ParseAsync(
                    innerTerm, shapeGraphMatch, dictionary, pathVocabulary, rdfListIds, cancellationToken).ConfigureAwait(false);
                return new ZeroOrOnePath(inner);
            }
        }

        throw new System.FormatException(
            $"Blank-node sh:path value {pathTerm} carries no path-operator predicate.");
    }

    //Sequence path — the blank node is an RDF list head; each member is a
    //sub-path.
    private static async Task<PropertyPath> ParseSequencePathAsync(
        TermId listHead,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        PathVocabularyIds pathVocabulary,
        RdfListIds rdfListIds,
        CancellationToken cancellationToken)
    {
        ImmutableArray<PropertyPath> steps = await ParsePathListAsync(
            listHead, shapeGraphMatch, dictionary, pathVocabulary, rdfListIds, cancellationToken).ConfigureAwait(false);

        if(steps.Length < 2)
        {
            throw new System.FormatException(
                "sh:path sequence list must contain at least two sub-paths.");
        }

        return new SequencePath(steps);
    }

    //Walk an RDF list of paths; parse each member as a path.
    private static async Task<ImmutableArray<PropertyPath>> ParsePathListAsync(
        TermId listHead,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        PathVocabularyIds pathVocabulary,
        RdfListIds rdfListIds,
        CancellationToken cancellationToken)
    {
        ImmutableArray<PropertyPath>.Builder builder = ImmutableArray.CreateBuilder<PropertyPath>();
        TermId cursor = listHead;

        while(cursor != rdfListIds.RdfNil)
        {
            TermId? first = null;
            TermId? rest = null;

            await foreach(EncodedTriple triple in shapeGraphMatch(cursor, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                if(triple.Predicate == rdfListIds.RdfFirst)
                {
                    first = triple.Object;
                }
                else if(triple.Predicate == rdfListIds.RdfRest)
                {
                    rest = triple.Object;
                }
            }

            if(first is null || rest is null)
            {
                throw new System.FormatException(
                    $"Malformed RDF list in sh:path: node {cursor} lacks rdf:first or rdf:rest.");
            }

            PropertyPath memberPath = await ParseAsync(
                first.Value, shapeGraphMatch, dictionary, pathVocabulary, rdfListIds, cancellationToken).ConfigureAwait(false);
            builder.Add(memberPath);

            cursor = rest.Value;
        }

        return builder.ToImmutable();
    }
}
