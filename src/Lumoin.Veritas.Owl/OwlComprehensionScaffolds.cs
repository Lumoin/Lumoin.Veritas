using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// Conclusion-guided comprehension minting: collects the contentful
/// class-expression scaffolds of a conclusion graph — the blank-rooted
/// expression structures whose nodes also carry claims outside the
/// expression grammar — and copies their grammar triples under fresh blank
/// labels, so an entailment check can add the copies to its premise. The
/// informative comprehension conditions of the OWL 2 RDF-Based Semantics
/// state that every such expression exists in every interpretation; the
/// minted copies realise exactly the finitely many expressions the
/// conclusion names, so the infinite schema never materialises.
/// </summary>
/// <remarks>
/// <para>
/// This is the complement of the pure-existence stripping the embedding
/// applies under <see cref="OwlComprehension.InformativeConditions"/>: a
/// scaffold every one of whose nodes occurs only inside the expression
/// grammar is granted at check time and needs no minting, while a scaffold
/// with a contentful mention — a subsumption, an equivalence, a membership,
/// a range — keeps that mention as a proof obligation and only the grammar
/// structure is granted here. The claims themselves must still be derived
/// by the closure's rules over the minted structure.
/// </para>
/// <para>
/// The grammar walked is the class-expression scaffold grammar: restriction
/// nodes with <c>owl:onProperty</c> and exactly one value constraint,
/// boolean nodes with exactly one of <c>owl:unionOf</c>,
/// <c>owl:intersectionOf</c>, <c>owl:oneOf</c>, or <c>owl:complementOf</c>,
/// nil-terminated acyclic member lists, nested expressions, and the
/// <c>owl:Class</c>, <c>rdfs:Class</c>, <c>owl:Restriction</c>, and
/// <c>rdf:List</c> typings. Exactly one constructor application per node is
/// the comprehension conditions' own shape — a node stated under two forms
/// at once is granted by neither. A blank list head hanging under a named
/// subject's boolean constructor is minted as list structure alone — the
/// constructor triple on the named subject asserts content about that
/// subject and is never granted.
/// </para>
/// <para>
/// The conditions quantify over classes and properties, so a minted
/// structure's named arguments must already be forced into that standing by
/// the premise: a named class position — a complement target, a filler, a
/// qualifier, a boolean member outside an enumeration — must be
/// class-forced, and a named <c>owl:onProperty</c> target property-forced,
/// each by a premise triple whose iff semantic condition demands it.
/// Enumeration members need only exist and are free. Non-grammar triples on
/// a reached node stay in the conclusion as content; a malformed, cyclic,
/// or unforced scaffold mints nothing from its root, which is always
/// sound — the conclusion merely stays unsettled. Fresh nodes are
/// engine-minted terms, disjoint by type from every blank node the
/// premise or the conclusion can spell, so a copy can never capture an
/// input node.
/// </para>
/// </remarks>
internal static class OwlComprehensionScaffolds
{
    /// <summary>The role a blank node plays inside a candidate scaffold walk.</summary>
    private enum MintRole
    {
        /// <summary>A class-expression root: a restriction or boolean class node.</summary>
        Expression = 0,

        /// <summary>An RDF list cell whose members are classes — a union or intersection member list.</summary>
        ClassListCell = 1,

        /// <summary>An RDF list cell whose members are individuals — an enumeration member list.</summary>
        IndividualListCell = 2,
    }

    /// <summary>The named premise nodes forced into class or property standing by the premise's own iff semantic conditions.</summary>
    /// <param name="Classes">The IRIs the premise forces into class standing.</param>
    /// <param name="Properties">The IRIs the premise forces into property standing.</param>
    internal readonly record struct ForcedStanding(HashSet<Utf8String> Classes, HashSet<Utf8String> Properties);

    /// <summary>
    /// Collects the conclusion's contentful scaffolds and returns their
    /// grammar triples copied under fresh blank labels that collide with no
    /// blank label of the premise or the conclusion; empty when the
    /// conclusion carries no mintable contentful scaffold.
    /// </summary>
    /// <param name="conclusion">The conclusion graph the scaffolds are read from.</param>
    /// <param name="premise">The premise graph forcing the named arguments' standing, whose blank labels the fresh labels also avoid.</param>
    /// <returns>The renamed scaffold copies, in the conclusion's triple order.</returns>
    public static List<Quad> MintContentful(IReadOnlyList<Quad> conclusion, IReadOnlyList<Quad> premise)
    {
        Dictionary<Utf8String, List<Quad>> bySubject = [];
        Dictionary<Utf8String, List<Quad>> occurrences = [];
        foreach(Quad quad in conclusion)
        {
            if(quad.Subject is BlankNode subject)
            {
                Append(bySubject, subject.Label, quad);
                Append(occurrences, subject.Label, quad);
            }

            if(quad.Object is BlankNode @object)
            {
                Append(occurrences, @object.Label, quad);
            }
        }

        ForcedStanding forced = CollectForcedStanding(premise);

        HashSet<Quad> minted = [];
        foreach(KeyValuePair<Utf8String, List<Quad>> candidate in bySubject)
        {
            if(HasConstructorPredicate(candidate.Value)
                && TryCollectScaffold(candidate.Key, MintRole.Expression, bySubject, forced, out HashSet<Quad>? scaffold, out Dictionary<Utf8String, MintRole>? nodes)
                && IsContentful(nodes, occurrences, scaffold))
            {
                minted.UnionWith(scaffold);
            }
        }

        //A blank list head under a named subject's boolean constructor is
        //reachable from no blank expression root, so it is walked here as
        //its own list scaffold; the constructor triple itself stays behind
        //as the content to prove, which also makes the scaffold contentful
        //by construction.
        foreach(Quad quad in conclusion)
        {
            if(quad.Subject is not BlankNode && quad.Object is BlankNode head)
            {
                Utf8String predicate = quad.Predicate.Iri;
                MintRole? memberRole = predicate switch
                {
                    _ when predicate.Equals(OwlVocabulary.UnionOf) || predicate.Equals(OwlVocabulary.IntersectionOf) => MintRole.ClassListCell,
                    _ when predicate.Equals(OwlVocabulary.OneOf) => MintRole.IndividualListCell,
                    _ => null,
                };

                if(memberRole is MintRole role
                    && TryCollectScaffold(head.Label, role, bySubject, forced, out HashSet<Quad>? listScaffold, out _))
                {
                    minted.UnionWith(listScaffold);
                }
            }
        }

        if(minted.Count == 0)
        {
            return [];
        }

        return Rename(conclusion, premise, minted);
    }

    /// <summary>
    /// Harvests the named nodes whose class or property standing the
    /// premise forces: a typing, a hierarchy or characteristic position, a
    /// class-valued object position, or use as a statement's predicate —
    /// each a triple whose iff semantic condition demands the standing in
    /// every satisfying interpretation.
    /// </summary>
    /// <param name="premise">The premise graph.</param>
    /// <returns>The forced class and property IRIs.</returns>
    internal static ForcedStanding CollectForcedStanding(IReadOnlyList<Quad> premise)
    {
        HashSet<Utf8String> classes = [];
        HashSet<Utf8String> properties = [];

        foreach(Quad quad in premise)
        {
            Utf8String predicate = quad.Predicate.Iri;
            properties.Add(predicate);

            if(predicate.Equals(Vocabulary.Rdf.Type) && quad.Object is NamedNode typing)
            {
                if(typing.Iri.Equals(OwlVocabulary.ClassTerm)
                    || typing.Iri.Equals(RdfVocabulary.Rdfs.Class)
                    || typing.Iri.Equals(OwlVocabulary.Restriction)
                    || typing.Iri.Equals(RdfVocabulary.Rdfs.Datatype))
                {
                    AddNamed(classes, quad.Subject);
                }
                else if(typing.Iri.Equals(RdfVocabulary.Rdf.Property)
                    || typing.Iri.Equals(OwlVocabulary.ObjectPropertyTerm)
                    || typing.Iri.Equals(OwlVocabulary.DatatypeProperty)
                    || typing.Iri.Equals(OwlVocabulary.AnnotationPropertyTerm)
                    || typing.Iri.Equals(OwlVocabulary.FunctionalProperty)
                    || typing.Iri.Equals(OwlVocabulary.InverseFunctionalProperty)
                    || typing.Iri.Equals(OwlVocabulary.TransitiveProperty)
                    || typing.Iri.Equals(OwlVocabulary.SymmetricProperty)
                    || typing.Iri.Equals(OwlVocabulary.AsymmetricProperty)
                    || typing.Iri.Equals(OwlVocabulary.ReflexiveProperty)
                    || typing.Iri.Equals(OwlVocabulary.IrreflexiveProperty))
                {
                    AddNamed(properties, quad.Subject);
                }
            }
            else if(predicate.Equals(RdfVocabulary.Rdfs.SubClassOf)
                || predicate.Equals(OwlVocabulary.EquivalentClass)
                || predicate.Equals(OwlVocabulary.ComplementOf)
                || predicate.Equals(OwlVocabulary.DisjointWith))
            {
                AddNamed(classes, quad.Subject);
                AddNamed(classes, quad.Object);
            }
            else if(predicate.Equals(OwlVocabulary.SomeValuesFrom)
                || predicate.Equals(OwlVocabulary.AllValuesFrom)
                || predicate.Equals(OwlVocabulary.OnClass)
                || predicate.Equals(OwlVocabulary.OnDataRange))
            {
                AddNamed(classes, quad.Object);
            }
            else if(predicate.Equals(RdfVocabulary.Rdfs.Domain) || predicate.Equals(RdfVocabulary.Rdfs.Range))
            {
                AddNamed(properties, quad.Subject);
                AddNamed(classes, quad.Object);
            }
            else if(predicate.Equals(OwlVocabulary.UnionOf) || predicate.Equals(OwlVocabulary.IntersectionOf) || predicate.Equals(OwlVocabulary.OneOf))
            {
                AddNamed(classes, quad.Subject);
            }
            else if(predicate.Equals(RdfVocabulary.Rdfs.SubPropertyOf)
                || predicate.Equals(OwlVocabulary.EquivalentProperty)
                || predicate.Equals(OwlVocabulary.InverseOf)
                || predicate.Equals(OwlVocabulary.PropertyDisjointWith))
            {
                AddNamed(properties, quad.Subject);
                AddNamed(properties, quad.Object);
            }
            else if(predicate.Equals(OwlVocabulary.OnProperty))
            {
                AddNamed(properties, quad.Object);
            }
        }

        return new ForcedStanding(classes, properties);
    }

    /// <summary>Adds a named term's IRI to a forced-standing set; any other term kind is ignored.</summary>
    /// <param name="standingToAppendTo">The forced-standing sink.</param>
    /// <param name="term">The term at the forcing position.</param>
    private static void AddNamed(HashSet<Utf8String> standingToAppendTo, RdfTerm term)
    {
        if(term is NamedNode named)
        {
            standingToAppendTo.Add(named.Iri);
        }
    }

    /// <summary>Whether the premise forces the named node into class standing; <c>owl:Thing</c> and <c>owl:Nothing</c> are classes in every interpretation.</summary>
    /// <param name="forced">The premise's forced standing.</param>
    /// <param name="named">The named node at a class position.</param>
    /// <returns><see langword="true"/> when the standing is forced.</returns>
    internal static bool IsClassForced(ForcedStanding forced, NamedNode named)
    {
        return named.Iri.Equals(OwlVocabulary.Thing)
            || named.Iri.Equals(OwlVocabulary.Nothing)
            || forced.Classes.Contains(named.Iri);
    }

    /// <summary>Whether the node's subject triples carry an expression constructor — a boolean-class predicate or a restriction's <c>owl:onProperty</c>.</summary>
    /// <param name="subjectTriples">The node's subject triples.</param>
    /// <returns><see langword="true"/> for a candidate expression root.</returns>
    private static bool HasConstructorPredicate(List<Quad> subjectTriples)
    {
        foreach(Quad quad in subjectTriples)
        {
            Utf8String predicate = quad.Predicate.Iri;
            if(predicate.Equals(OwlVocabulary.UnionOf)
                || predicate.Equals(OwlVocabulary.IntersectionOf)
                || predicate.Equals(OwlVocabulary.OneOf)
                || predicate.Equals(OwlVocabulary.ComplementOf)
                || predicate.Equals(OwlVocabulary.OnProperty))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks a candidate scaffold from its root, collecting the grammar
    /// triples of every reached node: each node must be a well-formed
    /// expression or list cell carrying exactly one constructor
    /// application, every named argument must carry its premise-forced
    /// standing, non-grammar triples are skipped as content, and the
    /// reachability among reached nodes must be acyclic. A malformed node,
    /// an unforced argument, a role conflict, or a cycle refuses the whole
    /// scaffold.
    /// </summary>
    /// <param name="root">The root blank's label.</param>
    /// <param name="rootRole">The role the root is walked as.</param>
    /// <param name="bySubject">Blank-subject triple index.</param>
    /// <param name="forced">The premise's forced standing.</param>
    /// <param name="scaffold">The collected grammar triples when the scaffold is well formed.</param>
    /// <param name="nodes">The reached nodes and their roles when the scaffold is well formed.</param>
    /// <returns><see langword="true"/> when the scaffold is well formed.</returns>
    private static bool TryCollectScaffold(
        Utf8String root,
        MintRole rootRole,
        Dictionary<Utf8String, List<Quad>> bySubject,
        ForcedStanding forced,
        [NotNullWhen(true)] out HashSet<Quad>? scaffold,
        [NotNullWhen(true)] out Dictionary<Utf8String, MintRole>? nodes)
    {
        scaffold = null;
        nodes = null;
        HashSet<Quad> collected = [];
        Dictionary<Utf8String, MintRole> roles = [];
        List<(Utf8String From, Utf8String To)> edges = [];
        Stack<(Utf8String Label, MintRole Role)> work = new();
        work.Push((root, rootRole));

        while(work.Count > 0)
        {
            (Utf8String label, MintRole role) = work.Pop();
            if(roles.TryGetValue(label, out MintRole existing))
            {
                if(existing != role)
                {
                    return false;
                }

                continue;
            }

            roles[label] = role;

            if(!bySubject.TryGetValue(label, out List<Quad>? triples))
            {
                return false;
            }

            bool valid = role switch
            {
                MintRole.Expression => CollectExpressionNode(label, triples, forced, collected, work, edges),
                MintRole.ClassListCell or MintRole.IndividualListCell => CollectListCell(label, role, triples, forced, collected, work, edges),
                _ => false
            };

            if(!valid)
            {
                return false;
            }
        }

        if(!IsAcyclic(roles, edges))
        {
            return false;
        }

        scaffold = collected;
        nodes = roles;

        return true;
    }

    /// <summary>
    /// Collects one expression node's grammar triples, enforcing exactly
    /// one constructor application: one boolean constructor with no
    /// restriction shape, or one <c>owl:onProperty</c> with exactly one
    /// value constraint. Typings among <c>owl:Class</c>, <c>rdfs:Class</c>,
    /// and <c>owl:Restriction</c> collect; qualifier classes ride only a
    /// qualified cardinality; named arguments must carry their
    /// premise-forced standing; every non-grammar triple is skipped as
    /// content.
    /// </summary>
    /// <param name="label">This node's blank label, the source of the recorded edges.</param>
    /// <param name="triples">The node's subject triples.</param>
    /// <param name="forced">The premise's forced standing.</param>
    /// <param name="collectedToAppendTo">The scaffold triple sink.</param>
    /// <param name="work">The walk stack.</param>
    /// <param name="edgesToAppendTo">The reachability edge sink, one <c>(from, to)</c> per pushed neighbour.</param>
    /// <returns><see langword="true"/> when the node is a well-formed expression.</returns>
    private static bool CollectExpressionNode(
        Utf8String label,
        List<Quad> triples,
        ForcedStanding forced,
        HashSet<Quad> collectedToAppendTo,
        Stack<(Utf8String Label, MintRole Role)> work,
        List<(Utf8String From, Utf8String To)> edgesToAppendTo)
    {
        int booleanConstructors = 0;
        int onPropertyCount = 0;
        int primaryConstraints = 0;
        int qualifierClasses = 0;
        bool qualifiedPrimary = false;

        foreach(Quad quad in triples)
        {
            Utf8String predicate = quad.Predicate.Iri;

            if(predicate.Equals(Vocabulary.Rdf.Type))
            {
                if(quad.Object is NamedNode typing
                    && (typing.Iri.Equals(OwlVocabulary.ClassTerm) || typing.Iri.Equals(RdfVocabulary.Rdfs.Class) || typing.Iri.Equals(OwlVocabulary.Restriction)))
                {
                    collectedToAppendTo.Add(quad);
                }

                continue;
            }

            if(predicate.Equals(OwlVocabulary.UnionOf) || predicate.Equals(OwlVocabulary.IntersectionOf))
            {
                booleanConstructors++;
                if(!PushListOrNil(label, quad.Object, MintRole.ClassListCell, work, edgesToAppendTo))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.OneOf))
            {
                booleanConstructors++;
                if(!PushListOrNil(label, quad.Object, MintRole.IndividualListCell, work, edgesToAppendTo))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.ComplementOf))
            {
                booleanConstructors++;
                if(quad.Object is BlankNode nested)
                {
                    work.Push((nested.Label, MintRole.Expression));
                    edgesToAppendTo.Add((label, nested.Label));
                }
                else if(quad.Object is not NamedNode complemented || !IsClassForced(forced, complemented))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.SomeValuesFrom) || predicate.Equals(OwlVocabulary.AllValuesFrom))
            {
                primaryConstraints++;
                if(quad.Object is BlankNode nestedFiller)
                {
                    work.Push((nestedFiller.Label, MintRole.Expression));
                    edgesToAppendTo.Add((label, nestedFiller.Label));
                }
                else if(quad.Object is not NamedNode filler || !IsClassForced(forced, filler))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.OnProperty))
            {
                onPropertyCount++;
                if(quad.Object is not NamedNode property || !forced.Properties.Contains(property.Iri))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.MinCardinality)
                || predicate.Equals(OwlVocabulary.MaxCardinality)
                || predicate.Equals(OwlVocabulary.Cardinality))
            {
                primaryConstraints++;
                if(quad.Object is not Literal)
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.MinQualifiedCardinality)
                || predicate.Equals(OwlVocabulary.MaxQualifiedCardinality)
                || predicate.Equals(OwlVocabulary.QualifiedCardinality))
            {
                primaryConstraints++;
                qualifiedPrimary = true;
                if(quad.Object is not Literal)
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.OnClass) || predicate.Equals(OwlVocabulary.OnDataRange))
            {
                qualifierClasses++;
                if(quad.Object is BlankNode nestedQualifier)
                {
                    work.Push((nestedQualifier.Label, MintRole.Expression));
                    edgesToAppendTo.Add((label, nestedQualifier.Label));
                }
                else if(quad.Object is not NamedNode qualifier || !IsClassForced(forced, qualifier))
                {
                    return false;
                }
            }
            else if(predicate.Equals(OwlVocabulary.HasValue) || predicate.Equals(OwlVocabulary.HasSelf))
            {
                //A blank hasValue object is an anonymous individual —
                //contentful, never granted structure.
                primaryConstraints++;
                if(quad.Object is BlankNode)
                {
                    return false;
                }
            }
            else
            {
                //A non-grammar triple is content: it stays in the
                //conclusion as a proof obligation and never mints.
                continue;
            }

            collectedToAppendTo.Add(quad);
        }

        return GrantsExactlyOneConstructorApplication(booleanConstructors, onPropertyCount, primaryConstraints, qualifierClasses, qualifiedPrimary);
    }

    /// <summary>
    /// The exactly-one-constructor grant gate shared by minting and the
    /// check-time stripper: a boolean-class node carries one boolean
    /// constructor and nothing else, and a restriction node carries one
    /// <c>owl:onProperty</c>, one primary constraint, and a qualifier
    /// class only beside a qualified cardinality. One gate, two callers —
    /// the discipline cannot drift between them.
    /// </summary>
    /// <param name="booleanConstructors">The boolean constructor applications counted on the node.</param>
    /// <param name="onPropertyCount">The <c>owl:onProperty</c> triples counted on the node.</param>
    /// <param name="primaryConstraints">The primary constraint predicates counted on the node.</param>
    /// <param name="qualifierClasses">The <c>owl:onClass</c>/<c>owl:onDataRange</c> qualifiers counted on the node.</param>
    /// <param name="qualifiedPrimary">Whether a qualified cardinality is among the primary constraints.</param>
    /// <returns><see langword="true"/> when the tallies state exactly one constructor application.</returns>
    internal static bool GrantsExactlyOneConstructorApplication(int booleanConstructors, int onPropertyCount, int primaryConstraints, int qualifierClasses, bool qualifiedPrimary)
    {
        bool booleanShape = booleanConstructors == 1 && onPropertyCount == 0 && primaryConstraints == 0 && qualifierClasses == 0;
        bool restrictionShape = booleanConstructors == 0
            && onPropertyCount == 1
            && primaryConstraints == 1
            && (qualifierClasses == 0 || (qualifiedPrimary && qualifierClasses == 1));

        return booleanShape || restrictionShape;
    }

    /// <summary>
    /// Collects one list cell's grammar triples: an optional
    /// <c>rdf:List</c> typing, exactly one <c>rdf:first</c> whose member
    /// obeys the cell's role — a class member must be a nested expression
    /// or a class-forced name, an enumeration member any named node or
    /// literal — and exactly one <c>rdf:rest</c> continuing the spine or
    /// terminating at <c>rdf:nil</c>; every non-grammar triple is skipped
    /// as content.
    /// </summary>
    /// <param name="label">This cell's blank label, the source of the recorded edges.</param>
    /// <param name="role">The cell's role, deciding the member obligation.</param>
    /// <param name="triples">The cell's subject triples.</param>
    /// <param name="forced">The premise's forced standing.</param>
    /// <param name="collectedToAppendTo">The scaffold triple sink.</param>
    /// <param name="work">The walk stack.</param>
    /// <param name="edgesToAppendTo">The reachability edge sink, one <c>(from, to)</c> per pushed neighbour.</param>
    /// <returns><see langword="true"/> when the cell is well formed.</returns>
    private static bool CollectListCell(
        Utf8String label,
        MintRole role,
        List<Quad> triples,
        ForcedStanding forced,
        HashSet<Quad> collectedToAppendTo,
        Stack<(Utf8String Label, MintRole Role)> work,
        List<(Utf8String From, Utf8String To)> edgesToAppendTo)
    {
        int firstCount = 0;
        int restCount = 0;

        foreach(Quad quad in triples)
        {
            Utf8String predicate = quad.Predicate.Iri;

            if(predicate.Equals(Vocabulary.Rdf.Type))
            {
                if(quad.Object is NamedNode typing && typing.Iri.Equals(RdfVocabulary.Rdf.List))
                {
                    collectedToAppendTo.Add(quad);
                }

                continue;
            }

            if(predicate.Equals(RdfVocabulary.Rdf.First))
            {
                firstCount++;
                if(role == MintRole.ClassListCell)
                {
                    if(quad.Object is BlankNode nested)
                    {
                        work.Push((nested.Label, MintRole.Expression));
                        edgesToAppendTo.Add((label, nested.Label));
                    }
                    else if(quad.Object is not NamedNode member || !IsClassForced(forced, member))
                    {
                        return false;
                    }
                }
                else if(quad.Object is not NamedNode and not Literal)
                {
                    //An enumeration member need only exist, but a blank
                    //member is an anonymous individual the conclusion binds —
                    //contentful, never granted structure.
                    return false;
                }
            }
            else if(predicate.Equals(RdfVocabulary.Rdf.Rest))
            {
                restCount++;
                if(!PushListOrNil(label, quad.Object, role, work, edgesToAppendTo))
                {
                    return false;
                }
            }
            else
            {
                continue;
            }

            collectedToAppendTo.Add(quad);
        }

        return firstCount == 1 && restCount == 1;
    }

    /// <summary>Pushes a list continuation onto the walk: a blank cell continues under the same role and records a reachability edge from its predecessor, <c>rdf:nil</c> terminates, anything else malforms.</summary>
    /// <param name="from">The predecessor node's blank label, the source of the recorded edge.</param>
    /// <param name="object">The list-position object term.</param>
    /// <param name="cellRole">The role the continuing cell keeps.</param>
    /// <param name="work">The walk stack.</param>
    /// <param name="edgesToAppendTo">The reachability edge sink.</param>
    /// <returns><see langword="true"/> when the term is a valid list continuation.</returns>
    private static bool PushListOrNil(Utf8String from, RdfTerm @object, MintRole cellRole, Stack<(Utf8String Label, MintRole Role)> work, List<(Utf8String From, Utf8String To)> edgesToAppendTo)
    {
        if(@object is BlankNode cell)
        {
            work.Push((cell.Label, cellRole));
            edgesToAppendTo.Add((from, cell.Label));

            return true;
        }

        return @object is NamedNode named && named.Iri.Equals(RdfVocabulary.Rdf.Nil);
    }

    /// <summary>
    /// Whether the recorded reachability among a scaffold's nodes is
    /// acyclic, by an iterative Kahn topological sort: a node reachable
    /// from itself never loses its last in-edge, so a remaining node
    /// witnesses a cycle; a shared sub-expression is a DAG join, not a
    /// cycle.
    /// </summary>
    /// <param name="nodes">The reached scaffold nodes keyed by label.</param>
    /// <param name="edges">The recorded reachability edges among those nodes.</param>
    /// <returns><see langword="true"/> when the reachability is acyclic.</returns>
    private static bool IsAcyclic(Dictionary<Utf8String, MintRole> nodes, List<(Utf8String From, Utf8String To)> edges)
    {
        Dictionary<Utf8String, int> inDegree = new(nodes.Count);
        Dictionary<Utf8String, List<Utf8String>> outgoing = new(nodes.Count);
        foreach(Utf8String node in nodes.Keys)
        {
            inDegree[node] = 0;
            outgoing[node] = [];
        }

        foreach((Utf8String from, Utf8String to) in edges)
        {
            outgoing[from].Add(to);
            inDegree[to] += 1;
        }

        Stack<Utf8String> ready = new();
        foreach(KeyValuePair<Utf8String, int> entry in inDegree)
        {
            if(entry.Value == 0)
            {
                ready.Push(entry.Key);
            }
        }

        int retired = 0;
        while(ready.Count > 0)
        {
            Utf8String node = ready.Pop();
            retired++;
            foreach(Utf8String next in outgoing[node])
            {
                int remaining = inDegree[next] - 1;
                inDegree[next] = remaining;
                if(remaining == 0)
                {
                    ready.Push(next);
                }
            }
        }

        return retired == nodes.Count;
    }

    /// <summary>
    /// Whether any reached node has a mention outside the collected
    /// scaffold — the contentful selection: a pure scaffold is granted at
    /// check time by the embedding's stripping and needs no minting.
    /// </summary>
    /// <param name="nodes">The reached scaffold nodes keyed by label.</param>
    /// <param name="occurrences">All-position blank occurrence index over the conclusion.</param>
    /// <param name="scaffold">The collected scaffold triples.</param>
    /// <returns><see langword="true"/> when the scaffold is contentful.</returns>
    private static bool IsContentful(Dictionary<Utf8String, MintRole> nodes, Dictionary<Utf8String, List<Quad>> occurrences, HashSet<Quad> scaffold)
    {
        foreach(Utf8String label in nodes.Keys)
        {
            if(occurrences.TryGetValue(label, out List<Quad>? mentions))
            {
                foreach(Quad mention in mentions)
                {
                    if(!scaffold.Contains(mention))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Copies the minted triples with every blank node replaced by a fresh
    /// engine-minted node — disjoint by type from every blank node an
    /// input graph can spell; the copies follow the conclusion's triple
    /// order and ordinals are assigned in first-encounter order, so equal
    /// inputs mint equal copies.
    /// </summary>
    /// <param name="conclusion">The conclusion graph, the iteration order.</param>
    /// <param name="premise">The premise graph the copies stand beside.</param>
    /// <param name="minted">The collected scaffold triples.</param>
    /// <returns>The renamed copies.</returns>
    private static List<Quad> Rename(IReadOnlyList<Quad> conclusion, IReadOnlyList<Quad> premise, HashSet<Quad> minted)
    {
        Dictionary<Utf8String, EngineNode> freshOf = [];
        int next = 0;
        List<Quad> copies = [];
        foreach(Quad quad in conclusion)
        {
            if(!minted.Contains(quad))
            {
                continue;
            }

            RdfTerm subject = Map(quad.Subject, freshOf, ref next);
            RdfTerm @object = Map(quad.Object, freshOf, ref next);
            copies.Add(new Quad(subject, quad.Predicate, @object, Graph: null));
        }

        return copies;
    }

    /// <summary>Maps one term: a blank node takes its scaffold's fresh engine-minted node, minted on first encounter; any other term copies verbatim.</summary>
    /// <param name="term">The term to map.</param>
    /// <param name="freshOf">The original-label-to-fresh-node map.</param>
    /// <param name="next">The fresh-node ordinal counter.</param>
    /// <returns>The mapped term.</returns>
    private static RdfTerm Map(RdfTerm term, Dictionary<Utf8String, EngineNode> freshOf, ref int next)
    {
        if(term is not BlankNode blank)
        {
            return term;
        }

        if(!freshOf.TryGetValue(blank.Label, out EngineNode? fresh))
        {
            fresh = new EngineNode(EngineNodeFamily.ComprehensionScaffold, (uint)next);
            next++;
            freshOf[blank.Label] = fresh;
        }

        return fresh;
    }

    /// <summary>Appends a quad to a label's index list, creating it on first contact.</summary>
    /// <param name="index">The index.</param>
    /// <param name="label">The blank label.</param>
    /// <param name="quad">The quad to record.</param>
    private static void Append(Dictionary<Utf8String, List<Quad>> index, Utf8String label, Quad quad)
    {
        if(!index.TryGetValue(label, out List<Quad>? list))
        {
            list = [];
            index[label] = list;
        }

        list.Add(quad);
    }
}
