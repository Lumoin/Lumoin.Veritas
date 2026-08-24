using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Discovers SPARQL-based constraint components (SHACL-SPARQL §6) declared in the shape graph and turns each into
/// a <see cref="ConstraintComponentInfo"/> the loader registers, so that a shape providing the component's
/// parameters gets a <see cref="SparqlComponentConstraint"/>.
/// </summary>
/// <remarks>
/// A constraint component is a node carrying a validator (<c>sh:validator</c>/<c>sh:nodeValidator</c>/
/// <c>sh:propertyValidator</c> → an <c>sh:SPARQLAskValidator</c> with <c>sh:ask</c> or an
/// <c>sh:SPARQLSelectValidator</c> with <c>sh:select</c>) plus <c>sh:parameter</c>s. A shape "uses" the component
/// by providing values for the component's parameters; the first non-optional parameter is treated as the
/// activating (primary) parameter so a shape lacking it is not validated. The parameter values are pre-bound into
/// the validator query at evaluation time under variables named after the local part of each parameter's
/// <c>sh:path</c> (e.g. <c>ex:lang</c> → <c>$lang</c>).
/// </remarks>
internal static class SparqlComponentLoader
{
    /// <summary>
    /// Discovers every SPARQL-based constraint component in the shape graph and returns a registration entry per
    /// component.
    /// </summary>
    /// <param name="shapeGraphMatch">The shape-graph triple source.</param>
    /// <param name="dictionary">The shared term dictionary.</param>
    /// <param name="ids">Pre-resolved SHACL vocabulary identifiers (for the prefix sub-graph and metadata).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>One <see cref="ConstraintComponentInfo"/> per discovered component (empty when there are none).</returns>
    public static async Task<IReadOnlyList<ConstraintComponentInfo>> DiscoverAsync(
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        ShaclPopulationIds ids,
        CancellationToken cancellationToken)
    {
        IriId parameterId = dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Parameter));
        IriId pathId = dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Path));
        IriId optionalId = dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Optional));
        IriId validatorId = dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Validator));
        IriId nodeValidatorId = dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.NodeValidator));
        IriId propertyValidatorId = dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.PropertyValidator));
        IriId askId = dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Ask));

        //A component is any node carrying a validator link. Collect those subjects.
        HashSet<TermId> componentNodes = [];
        foreach(IriId validatorLink in new[] { validatorId, nodeValidatorId, propertyValidatorId })
        {
            await foreach(EncodedTriple triple in shapeGraphMatch(TermId.None, validatorLink, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                componentNodes.Add(triple.Subject);
            }
        }

        List<ConstraintComponentInfo> infos = [];
        foreach(TermId componentNode in componentNodes)
        {
            //The component IRI is emitted as sh:sourceConstraintComponent, so a blank-node component cannot be used.
            if(dictionary.Resolve(componentNode) is not NamedNode componentName)
            {
                continue;
            }

            SparqlComponentDefinition? definition = await BuildDefinitionAsync(
                componentNode, componentName.Iri, shapeGraphMatch, dictionary, ids,
                parameterId, pathId, optionalId, validatorId, nodeValidatorId, propertyValidatorId, askId, cancellationToken).ConfigureAwait(false);

            if(definition is null || definition.Parameters.IsDefaultOrEmpty)
            {
                continue;
            }

            infos.Add(BuildInfo(definition, dictionary));
        }

        return infos;
    }

    /// <summary>Reads a component node's parameters and validators into a <see cref="SparqlComponentDefinition"/>; returns <see langword="null"/> when no validator parses.</summary>
    private static async Task<SparqlComponentDefinition?> BuildDefinitionAsync(
        TermId componentNode,
        Utf8String componentIri,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        ShaclPopulationIds ids,
        IriId parameterId,
        IriId pathId,
        IriId optionalId,
        IriId validatorId,
        IriId nodeValidatorId,
        IriId propertyValidatorId,
        IriId askId,
        CancellationToken cancellationToken)
    {
        List<TermId> parameterNodes = [];
        List<TermId> genericValidatorNodes = [];
        List<TermId> nodeValidatorNodes = [];
        List<TermId> propertyValidatorNodes = [];

        await foreach(EncodedTriple triple in shapeGraphMatch(componentNode, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            IriId predicate = IriId.FromUnchecked(triple.Predicate);
            if(predicate.Equals(parameterId))
            {
                parameterNodes.Add(triple.Object);
            }
            else if(predicate.Equals(validatorId))
            {
                genericValidatorNodes.Add(triple.Object);
            }
            else if(predicate.Equals(nodeValidatorId))
            {
                nodeValidatorNodes.Add(triple.Object);
            }
            else if(predicate.Equals(propertyValidatorId))
            {
                propertyValidatorNodes.Add(triple.Object);
            }
        }

        ImmutableArray<SparqlComponentParameter>.Builder parameters = ImmutableArray.CreateBuilder<SparqlComponentParameter>();
        foreach(TermId parameterNode in parameterNodes)
        {
            SparqlComponentParameter? parameter = await ReadParameterAsync(parameterNode, shapeGraphMatch, dictionary, pathId, optionalId, cancellationToken).ConfigureAwait(false);
            if(parameter is not null)
            {
                parameters.Add(parameter);
            }
        }

        SparqlComponentValidator? generic = await ReadValidatorAsync(genericValidatorNodes, shapeGraphMatch, dictionary, ids, askId, cancellationToken).ConfigureAwait(false);
        SparqlComponentValidator? node = await ReadValidatorAsync(nodeValidatorNodes, shapeGraphMatch, dictionary, ids, askId, cancellationToken).ConfigureAwait(false);
        SparqlComponentValidator? property = await ReadValidatorAsync(propertyValidatorNodes, shapeGraphMatch, dictionary, ids, askId, cancellationToken).ConfigureAwait(false);

        if(generic is null && node is null && property is null)
        {
            return null;
        }

        return new SparqlComponentDefinition(componentIri, parameters.ToImmutable(), generic, node, property);
    }

    /// <summary>Reads one <c>sh:parameter</c> node: its <c>sh:path</c> predicate, its optionality, and the variable name (the path's local part).</summary>
    private static async Task<SparqlComponentParameter?> ReadParameterAsync(
        TermId parameterNode,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        IriId pathId,
        IriId optionalId,
        CancellationToken cancellationToken)
    {
        TermId? path = null;
        bool optional = false;
        await foreach(EncodedTriple triple in shapeGraphMatch(parameterNode, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            IriId predicate = IriId.FromUnchecked(triple.Predicate);
            if(predicate.Equals(pathId) && dictionary.Resolve(triple.Object) is NamedNode)
            {
                path = triple.Object;
            }
            else if(predicate.Equals(optionalId) && dictionary.Resolve(triple.Object) is Literal optionalLiteral)
            {
                optional = optionalLiteral.Value.Span.SequenceEqual("true"u8) || optionalLiteral.Value.Span.SequenceEqual("1"u8);
            }
        }

        if(path is not TermId pathTerm || dictionary.Resolve(pathTerm) is not NamedNode pathName)
        {
            return null;
        }

        return new SparqlComponentParameter(IriId.FromUnchecked(pathTerm), LocalName(pathName.Iri), optional);
    }

    /// <summary>Reads the first validator node of a kind: its <c>sh:ask</c>/<c>sh:select</c> query, parsed with its <c>sh:prefixes</c>, plus <c>sh:message</c>. Returns <see langword="null"/> when absent or unparseable.</summary>
    private static async Task<SparqlComponentValidator?> ReadValidatorAsync(
        List<TermId> validatorNodes,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        ShaclPopulationIds ids,
        IriId askId,
        CancellationToken cancellationToken)
    {
        foreach(TermId validatorNode in validatorNodes)
        {
            Utf8String? askText = null;
            Utf8String? selectText = null;
            ImmutableDictionary<string, string>.Builder messages = ImmutableDictionary.CreateBuilder<string, string>();
            List<TermId> prefixSubjects = [];

            await foreach(EncodedTriple triple in shapeGraphMatch(validatorNode, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                IriId predicate = IriId.FromUnchecked(triple.Predicate);
                if(predicate.Equals(askId) && dictionary.Resolve(triple.Object) is Literal askLiteral)
                {
                    askText = askLiteral.Value;
                }
                else if(predicate.Equals(ids.Select) && dictionary.Resolve(triple.Object) is Literal selectLiteral)
                {
                    selectText = selectLiteral.Value;
                }
                else if(predicate.Equals(ids.Message) && dictionary.Resolve(triple.Object) is Literal messageLiteral)
                {
                    string language = messageLiteral.Language is { } tag ? tag.ToString() : string.Empty;
                    messages[language] = messageLiteral.Value.ToString();
                }
                else if(predicate.Equals(ids.Prefixes))
                {
                    prefixSubjects.Add(triple.Object);
                }
            }

            bool isAsk = askText is not null;
            Utf8String? queryText = askText ?? selectText;
            if(queryText is not Utf8String text)
            {
                continue;
            }

            SparqlQuery? query = await SparqlQueryParsing.ParseAsync(
                text, prefixSubjects, shapeGraphMatch, dictionary, ids.Declare, ids.Prefix, ids.Namespace, cancellationToken).ConfigureAwait(false);
            if(query is null)
            {
                continue;
            }

            return new SparqlComponentValidator(isAsk, query, messages.ToImmutable());
        }

        return null;
    }

    /// <summary>Builds the <see cref="ConstraintComponentInfo"/> for a component: the first non-optional parameter activates it; the factory captures the shape's parameter values into a <see cref="SparqlComponentConstraint"/>.</summary>
    private static ConstraintComponentInfo BuildInfo(SparqlComponentDefinition definition, TermDictionary dictionary)
    {
        //Activate on the first non-optional parameter (so a shape lacking it is not validated); fall back to the
        //first parameter when every parameter is optional.
        SparqlComponentParameter primary = definition.Parameters[0];
        foreach(SparqlComponentParameter candidate in definition.Parameters)
        {
            if(!candidate.Optional)
            {
                primary = candidate;

                break;
            }
        }

        Utf8String primaryPath = ((NamedNode)dictionary.Resolve((TermId)primary.Path)).Iri;
        ImmutableArray<Utf8String>.Builder companions = ImmutableArray.CreateBuilder<Utf8String>();
        foreach(SparqlComponentParameter parameter in definition.Parameters)
        {
            if(!parameter.Path.Equals(primary.Path))
            {
                companions.Add(((NamedNode)dictionary.Resolve((TermId)parameter.Path)).Iri);
            }
        }

        return ConstraintComponentInfo.Create(
            definition.ComponentIri,
            primaryPath,
            new SparqlComponentFactory(definition).Build,
            companions.ToImmutable().AsSpan());
    }

    /// <summary>Captures a shape's parameter values (primary + companions) into a <see cref="SparqlComponentConstraint"/>.</summary>
    private static SparqlComponentConstraint BuildConstraint(ParameterBag bag, SparqlComponentDefinition definition)
    {
        ImmutableDictionary<IriId, TermId>.Builder values = ImmutableDictionary.CreateBuilder<IriId, TermId>();
        values[bag.PrimaryParameter] = bag.PrimaryValue;
        foreach(KeyValuePair<IriId, TermId> entry in bag.EnumerateCompanionScalars())
        {
            values[entry.Key] = entry.Value;
        }

        return new SparqlComponentConstraint(definition, values.ToImmutable());
    }

    /// <summary>
    /// Builds a SPARQL component constraint from a parameter bag, carrying the component definition as
    /// explicit state so the <see cref="ConstraintComponentFactory"/> is a bound method group rather than
    /// a lambda closing over the enclosing definition.
    /// </summary>
    /// <param name="definition">The SPARQL component definition the constraint is built from.</param>
    private sealed class SparqlComponentFactory(SparqlComponentDefinition definition)
    {
        /// <summary>The SPARQL component definition the constraint is built from.</summary>
        private SparqlComponentDefinition Definition { get; } = definition;

        /// <summary>Builds the constraint for a shape's parameter values.</summary>
        /// <param name="bag">The shape's parameter values.</param>
        /// <returns>The built constraint.</returns>
        public SparqlComponentConstraint Build(ParameterBag bag)
        {
            return BuildConstraint(bag, Definition);
        }
    }

    /// <summary>Returns the local part of an IRI (the text after the last <c>#</c> or <c>/</c>), used as the SPARQL variable a parameter value pre-binds to.</summary>
    /// <param name="iri">The parameter's <c>sh:path</c> IRI.</param>
    /// <returns>The local part.</returns>
    private static Utf8String LocalName(Utf8String iri)
    {
        ReadOnlySpan<byte> span = iri.Span;
        int cut = -1;
        for(int i = span.Length - 1; i >= 0; i--)
        {
            if(span[i] is (byte)'#' or (byte)'/')
            {
                cut = i;

                break;
            }
        }

        return cut < 0 ? iri : new Utf8String(span[(cut + 1)..].ToArray());
    }
}
