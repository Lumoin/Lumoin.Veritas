using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Phase 1 of the shape loader. Discovers every term that plays the role
/// of a shape in a shape graph and classifies each as a node shape or
/// property shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> The discovery rules follow SHACL 1.2 Core §3.1.1 — a
/// term is a shape when it is the object of <c>rdf:type</c> with one of
/// <c>sh:NodeShape</c>, <c>sh:PropertyShape</c>, <c>sh:Shape</c>, or
/// <c>sh:ShapeClass</c>; when it is the object of a shape-referencing
/// predicate (<c>sh:property</c>, <c>sh:node</c>, <c>sh:not</c>,
/// <c>sh:and</c>, <c>sh:or</c>, <c>sh:xone</c>,
/// <c>sh:qualifiedValueShape</c>, <c>sh:reifierShape</c>,
/// <c>sh:memberShape</c>); when it is the subject of a constraint
/// parameter or a target predicate; or when it carries <c>sh:path</c>
/// (which additionally identifies it as a property shape).
/// </para>
/// <para>
/// <b>Shape kind.</b> A term is classified as a property shape when any
/// of these is true: it carries <c>sh:path</c>, or it is reachable
/// through <c>sh:property</c>. Otherwise it is a node shape. This is the
/// rule from Core §2.1.1 and §2.1.2. Nothing else disambiguates.
/// </para>
/// <para>
/// <b>Scale-aware querying.</b> Every lookup issues a targeted
/// <see cref="StorageDelegates.MatchTriplesAsync"/> query against a
/// known predicate (e.g.
/// <c>dataMatch(null, rdfTypeId, nodeShapeClassId, ct)</c>) and streams
/// the results. The discovery phase never issues a
/// <c>Match(null, null, null)</c>. Memory use is O(number of shapes),
/// not O(size of shape graph) — suitable for shape graphs delivered
/// through a mmap-backed or remote
/// <see cref="StorageDelegates.MatchTriplesAsync"/>.
/// </para>
/// <para>
/// <b>RDF lists.</b> When a shape-list predicate
/// (<c>sh:and</c>/<c>sh:or</c>/<c>sh:xone</c>/<c>sh:qualifiedValueShape</c>)
/// has a list head as its object, the list head itself is <em>not</em>
/// a shape — its members are. Discovery probes each shape-reference
/// object for an outgoing <c>rdf:first</c> edge; if present, the
/// object is a list head and discovery skips it. List members are
/// independently discovered through the other rules (they carry
/// <c>sh:path</c>, <c>rdf:type sh:NodeShape</c>, constraint parameters,
/// or are themselves the object of a non-list shape-reference).
/// Genuine blank-node shape references — a blank node whose content
/// is a shape rather than a list — have no <c>rdf:first</c> edge and
/// are correctly recorded.
/// </para>
/// </remarks>
internal static class ShapeDiscovery
{
    /// <summary>
    /// Runs the discovery phase against <paramref name="shapeGraphMatch"/>
    /// and returns a builder for every discovered shape.
    /// </summary>
    /// <param name="shapeGraphMatch">Triple-match delegate over the shape graph.</param>
    /// <param name="shaclCoreIds">Pre-resolved SHACL core vocabulary term ids.</param>
    /// <param name="shaclConstraintIds">
    /// Pre-resolved SHACL constraint-parameter term ids; the subject side
    /// of any constraint triple identifies a shape.
    /// </param>
    /// <param name="rdfsVocabulary">
    /// Pre-resolved RDF/RDFS vocabulary term ids, for <c>rdf:type</c>
    /// lookups.
    /// </param>
    /// <param name="rdfListIds">
    /// Pre-resolved <c>rdf:first/rest/nil</c> term ids, used to detect
    /// when the object of a shape-list predicate
    /// (<c>sh:and</c>/<c>sh:or</c>/<c>sh:xone</c>/<c>sh:qualifiedValueShape</c>)
    /// is a list head rather than a single shape. List heads are not
    /// shapes and are skipped by rule 5; their members are discovered
    /// through the other rules.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>
    /// A dictionary keyed by shape term id, one entry per discovered
    /// shape.
    /// </returns>
    public static async Task<Dictionary<TermId, ShapeBuilder>> DiscoverAsync(
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        ShaclDiscoveryIds shaclCoreIds,
        IReadOnlyList<IriId> shaclConstraintIds,
        RdfsDiscoveryIds rdfsVocabulary,
        RdfListIds rdfListIds,
        CancellationToken cancellationToken = default)
    {
        Dictionary<TermId, ShapeBuilder> builders = [];

        //Rule 1: sh:path — subject is a property shape.
        //Do this first; subsequent rules must know whether a term is
        //already known to be a property shape so the classification
        //doesn't flip.
        await foreach(EncodedTriple triple in shapeGraphMatch(TermId.None, shaclCoreIds.Path, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            TermId subject = triple.Subject;
            if(!builders.ContainsKey(subject))
            {
                builders.Add(subject, new ShapeBuilder(subject, isPropertyShape: true));
            }
        }

        //Rule 2: rdf:type sh:{NodeShape,PropertyShape,Shape,ShapeClass}.
        foreach(IriId shapeClass in new[]
        {
            shaclCoreIds.NodeShapeClass,
            shaclCoreIds.PropertyShapeClass,
            shaclCoreIds.ShapeClass,
            shaclCoreIds.ShapeClassClass,
        })
        {
            await foreach(EncodedTriple triple in shapeGraphMatch(TermId.None, rdfsVocabulary.RdfType, shapeClass, cancellationToken).ConfigureAwait(false))
            {
                TermId subject = triple.Subject;
                //Classification: if sh:path already marked it as a property shape, keep that.
                //If it was typed as sh:PropertyShape, it's still a property shape.
                //Otherwise default to node shape.
                bool isPropertyShape =
                    (builders.TryGetValue(subject, out ShapeBuilder? existing) && existing.IsPropertyShape)
                    || shapeClass.Equals(shaclCoreIds.PropertyShapeClass);

                if(existing is null)
                {
                    builders.Add(subject, new ShapeBuilder(subject, isPropertyShape));
                }
            }
        }

        //Rule 3: subject of any target predicate.
        //A term that carries a target is a shape. Kind is determined by
        //whether it also carries sh:path (handled above) — otherwise it
        //defaults to node shape here.
        foreach(IriId targetPredicate in shaclCoreIds.TargetPredicates)
        {
            await foreach(EncodedTriple triple in shapeGraphMatch(TermId.None, targetPredicate, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                TermId subject = triple.Subject;
                if(!builders.ContainsKey(subject))
                {
                    builders.Add(subject, new ShapeBuilder(subject, isPropertyShape: false));
                }
            }
        }

        //Rule 4: subject of any constraint parameter.
        //A term carrying any sh:minCount, sh:datatype, ... is a shape.
        //Kind: node shape unless already known as property shape.
        foreach(IriId constraintPredicate in shaclConstraintIds)
        {
            await foreach(EncodedTriple triple in shapeGraphMatch(TermId.None, constraintPredicate, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                TermId subject = triple.Subject;
                if(!builders.ContainsKey(subject))
                {
                    builders.Add(subject, new ShapeBuilder(subject, isPropertyShape: false));
                }
            }
        }

        //Rule 5: object of a shape-referencing predicate.
        //sh:property — object is a property shape.
        //sh:node/sh:not/sh:reifierShape/sh:memberShape — object is a shape
        //    of the implied kind (property/node).
        //sh:and/sh:or/sh:xone/sh:qualifiedValueShape — object can be a
        //    list head or a single shape; the population phase walks
        //    lists and discovers members.
        //
        //For now we surface direct objects. List-member discovery
        //happens in ShapePopulation, which can add builders to the
        //dictionary when it encounters a previously-unknown shape-list
        //member.
        await foreach(EncodedTriple triple in shapeGraphMatch(TermId.None, shaclCoreIds.PropertyPredicate, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            TermId objectTerm = triple.Object;
            if(!builders.ContainsKey(objectTerm))
            {
                builders.Add(objectTerm, new ShapeBuilder(objectTerm, isPropertyShape: true));
            }
        }

        foreach(IriId shapeRefPredicate in shaclCoreIds.ShapeReferencePredicates)
        {
            await foreach(EncodedTriple triple in shapeGraphMatch(TermId.None, shapeRefPredicate, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                TermId objectTerm = triple.Object;
                if(builders.ContainsKey(objectTerm))
                {
                    continue;
                }

                //rdf:nil is the empty-list terminator. A shape-list
                //predicate (sh:and, sh:or, sh:xone, sh:qualifiedValueShape)
                //pointing at rdf:nil means an empty list of shapes —
                //semantically unusual but legal, and rdf:nil itself is
                //never a shape.
                if(objectTerm.Equals((TermId)rdfListIds.RdfNil))
                {
                    continue;
                }

                //The object may be a shape (the usual case — sh:node
                //someShape) or a non-empty list head (sh:and etc.
                //pointing at a list of shapes). A list head is not a
                //shape; skip it. Detect by probing for an outgoing
                //rdf:first edge: non-empty list cells always have
                //rdf:first; shapes do not.
                if(await HasOutgoingFirstAsync(
                    objectTerm,
                    shapeGraphMatch,
                    rdfListIds.RdfFirst,
                    cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                //Default to node shape. Property-shape objects were
                //already caught via the sh:property rule above with
                //isPropertyShape: true.
                builders.Add(objectTerm, new ShapeBuilder(objectTerm, isPropertyShape: false));
            }
        }

        return builders;
    }

    //Probes for an outgoing rdf:first edge on the given term. Returns
    //true if the term is a list cell; false otherwise. Used by rule 5
    //to distinguish list heads from shape references.
    private static async Task<bool> HasOutgoingFirstAsync(
        TermId term,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        IriId rdfFirst,
        CancellationToken cancellationToken)
    {
        await foreach(EncodedTriple _ in shapeGraphMatch(term, rdfFirst, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }
}
