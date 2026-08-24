using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Validation.Evaluators;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// A pre-populated <see cref="ConstraintEvaluatorRegistry"/> wiring
/// every built-in SHACL constraint component that has an evaluator
/// implementation. Consumers use this as the default registry passed
/// to <see cref="ShaclValidator.ValidateAsync"/>.
/// </summary>
/// <remarks>
/// Components without a registered evaluator fall through to
/// <see cref="NotImplementedEvaluator.EvaluateAsync"/>; the validator
/// still produces a usable report, flagging unimplemented components
/// with informational severity. Use
/// <see cref="ConstraintEvaluatorRegistry.With"/> or
/// <see cref="ConstraintEvaluatorRegistry.WithMany"/> to extend.
/// </remarks>
public static class ShaclBuiltInEvaluators
{
    /// <summary>
    /// The registry of built-in evaluators, built once at class load
    /// time and immutable thereafter. Safe to share between
    /// concurrent validation runs.
    /// </summary>
    public static ConstraintEvaluatorRegistry All { get; } = Build();

    private static ConstraintEvaluatorRegistry Build()
        => new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            //Cardinality and value-node-set shape.
            [ShaclComponentVocabulary.MinCount] = MinCountEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.MaxCount] = MaxCountEvaluator.EvaluateAsync,

            //Per-value kind, datatype, and value-set checks.
            [ShaclComponentVocabulary.NodeKind] = NodeKindEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Datatype] = DatatypeEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Class] = ClassEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Pattern] = PatternEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.In] = InEvaluator.EvaluateAsync,

            //String-shape constraints (2C-d batch 1).
            [ShaclComponentVocabulary.MinLength] = MinLengthEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.MaxLength] = MaxLengthEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.SingleLine] = SingleLineEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.LanguageIn] = LanguageInEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.UniqueLang] = UniqueLanguageEvaluator.EvaluateAsync,

            //Numeric range constraints (2C-d batch 2). All four route
            //through RdfValueComparer for SPARQL-defined cross-type
            //ordering covering numeric, string, boolean, datetime,
            //and duration value spaces.
            [ShaclComponentVocabulary.MinInclusive] = MinInclusiveEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.MaxInclusive] = MaxInclusiveEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.MinExclusive] = MinExclusiveEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.MaxExclusive] = MaxExclusiveEvaluator.EvaluateAsync,

            //Pair-property and value-set constraints (2C-d batch 3).
            //sh:hasValue uses term equality; sh:equals/sh:disjoint use
            //term equality; sh:lessThan/sh:lessThanOrEquals use
            //SPARQL ordering via RdfValueComparer. sh:closed is a
            //node-shape-level closure check.
            //
            //ShaclComponentVocabulary.EqualsTo is the project's name
            //for the sh:equals component IRI key, avoiding the
            //object.Equals collision.
            [ShaclComponentVocabulary.HasValue] = HasValueEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.EqualsTo] = EqualsEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Disjoint] = DisjointEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.LessThan] = LessThanEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.LessThanOrEquals] = LessThanOrEqualsEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Closed] = ClosedEvaluator.EvaluateAsync,

            //Shape-recursion constraints. Inner validation goes
            //through ValidationContext.ShapeValidator.
            [ShaclComponentVocabulary.Node] = NodeEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Property] = PropertyEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.And] = AndEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Or] = OrEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Not] = NotEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.Xone] = XoneEvaluator.EvaluateAsync,

            //Qualified-cardinality shape-recursion constraints
            //(2C-d batch 4). Both share QualifiedValueShapeCounting
            //for value counting and sibling-disjoint subtraction;
            //each emits violations against its own bound.
            [ShaclComponentVocabulary.QualifiedMinCount] = QualifiedMinCountEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.QualifiedMaxCount] = QualifiedMaxCountEvaluator.EvaluateAsync,

            //SHACL 1.2 list-cardinality constraints (batch 5b).
            //Each value node that is a SHACL list must satisfy the
            //corresponding bound; non-list value nodes are out of
            //scope per §6.12. List walking goes through the shared
            //RdfCollection.ToListAsync utility.
            [ShaclComponentVocabulary.MinListLength] = MinListLengthEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.MaxListLength] = MaxListLengthEvaluator.EvaluateAsync,

            //SHACL 1.2 list-member constraints (batch 5b pair 2).
            //sh:uniqueMembers prohibits repeated members in any
            //value-node list; sh:memberShape recurses each list
            //member through the shape registry. Non-list value
            //nodes are out of scope, matching the list-length pair.
            [ShaclComponentVocabulary.UniqueMembers] = UniqueMembersEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.MemberShape] = MemberShapeEvaluator.EvaluateAsync,

            //SHACL 1.2 class-hierarchy and pair-property constraints
            //(batch 5b pair 3). sh:rootClass walks rdfs:subClassOf*
            //via the existing TraversalPrimitives + RdfAdjacencyAdapter
            //machinery; sh:subsetOf mirrors DisjointEvaluator's pair-
            //property pattern with an inverted membership test.
            [ShaclComponentVocabulary.RootClass] = RootClassEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.SubsetOf] = SubsetOfEvaluator.EvaluateAsync,

            //SHACL 1.2 graph-global key uniqueness and RDF 1.2
            //reification (batch 5b pair 4 — closes the SHACL Core
            //dispatch gap). sh:uniqueValuesFor queries the data graph
            //backwards per value to find collisions with other
            //focuses; sh:reifierShape is a stub pending RDF 1.2
            //triple-term plumbing.
            [ShaclComponentVocabulary.UniqueValuesFor] = UniqueValuesForEvaluator.EvaluateAsync,
            [ShaclComponentVocabulary.ReifierShape] = ReifierShapeEvaluator.EvaluateAsync,

            //SHACL-SPARQL constraints. The constraint's SELECT query runs against the data graph with $this
            //pre-bound to the focus node; each result row is a violation (SHACL-SPARQL §5).
            [ShaclComponentVocabulary.SparqlConstraint] = SparqlConstraintEvaluator.EvaluateAsync,
        });
}
