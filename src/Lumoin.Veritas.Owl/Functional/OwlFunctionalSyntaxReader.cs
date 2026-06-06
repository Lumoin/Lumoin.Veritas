using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// Reads an OWL 2 functional-style syntax document
/// (<see href="https://www.w3.org/TR/owl2-syntax/">OWL 2 Structural Specification</see>)
/// into the same structural model the RDF mapping produces — a second
/// front-end for <see cref="OwlOntologyDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// The reader is value-based: malformedness is recorded in the document's
/// diagnostics and reading continues where structure permits. Parsing runs
/// in three explicit-stack passes — tokens, an S-expression tree, then a
/// post-order conversion of constructor groups to axioms and expressions —
/// so arbitrarily nested class expressions never touch the call stack.
/// </para>
/// <para>
/// Axiom and ontology annotations are recognised and consumed; they carry
/// no logical content for the profile and rule consumers this front-end
/// serves. Anonymous individuals (<c>_:label</c>) map to blank nodes.
/// </para>
/// </remarks>
public static class OwlFunctionalSyntaxReader
{
    /// <summary>
    /// Reads a functional-syntax ontology document.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The structural document; parse errors are on its diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static OwlOntologyDocument Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ReaderContext context = new();

        List<Token> tokens = Tokenize(text, context.Diagnostics);
        SExpr root = BuildTree(tokens, context.Diagnostics);
        context.ConvertDocument(root);

        return new OwlOntologyDocument(
            context.Axioms.ToImmutable(),
            context.OntologyIri,
            context.Diagnostics,
            context.DeclaredClasses,
            context.DeclaredObjectProperties,
            context.DeclaredDataProperties,
            context.DeclaredAnnotationProperties,
            context.DeclaredDatatypes);
    }

    //Tokenizer.

    private enum TokenKind
    {
        Open = 0,
        Close = 1,
        Equals = 2,
        Iri = 3,
        Name = 4,
        BlankNode = 5,
        Literal = 6,
        Number = 7,
    }

    private readonly record struct Token(TokenKind Kind, string Text, string? LiteralDatatype, string? LiteralLanguage);

    private static List<Token> Tokenize(string text, DiagnosticBag diagnostics)
    {
        List<Token> tokens = [];
        int i = 0;

        while(i < text.Length)
        {
            char c = text[i];

            if(char.IsWhiteSpace(c))
            {
                i++;

                continue;
            }

            if(c == '#')
            {
                while(i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if(c == '(')
            {
                tokens.Add(new Token(TokenKind.Open, "(", null, null));
                i++;

                continue;
            }

            if(c == ')')
            {
                tokens.Add(new Token(TokenKind.Close, ")", null, null));
                i++;

                continue;
            }

            if(c == '=')
            {
                tokens.Add(new Token(TokenKind.Equals, "=", null, null));
                i++;

                continue;
            }

            if(c == '<')
            {
                int end = text.IndexOf('>', i + 1);
                if(end < 0)
                {
                    Report(diagnostics, "Unterminated IRI reference.");

                    return tokens;
                }

                tokens.Add(new Token(TokenKind.Iri, text[(i + 1)..end], null, null));
                i = end + 1;

                continue;
            }

            if(c == '"')
            {
                StringBuilder value = new();
                i++;
                while(i < text.Length && text[i] != '"')
                {
                    if(text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        value.Append(text[i] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            _ => text[i]
                        });
                    }
                    else
                    {
                        value.Append(text[i]);
                    }

                    i++;
                }

                if(i == text.Length)
                {
                    Report(diagnostics, "Unterminated string literal.");

                    return tokens;
                }

                i++;

                string? datatype = null;
                string? language = null;
                if(i + 1 < text.Length && text[i] == '^' && text[i + 1] == '^')
                {
                    i += 2;
                    (datatype, i) = ReadNameOrIri(text, i, diagnostics);
                }
                else if(i < text.Length && text[i] == '@')
                {
                    i++;
                    int start = i;
                    while(i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '-'))
                    {
                        i++;
                    }

                    language = text[start..i];
                }

                tokens.Add(new Token(TokenKind.Literal, value.ToString(), datatype, language));

                continue;
            }

            if(c == '_' && i + 1 < text.Length && text[i + 1] == ':')
            {
                int start = i + 2;
                int end = start;
                while(end < text.Length && IsNameChar(text[end]))
                {
                    end++;
                }

                tokens.Add(new Token(TokenKind.BlankNode, text[start..end], null, null));
                i = end;

                continue;
            }

            if(char.IsDigit(c))
            {
                int start = i;
                while(i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..i], null, null));

                continue;
            }

            if(IsNameStartChar(c) || c == ':')
            {
                int start = i;
                while(i < text.Length && (IsNameChar(text[i]) || text[i] == ':'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Name, text[start..i], null, null));

                continue;
            }

            Report(diagnostics, $"Unexpected character '{c}'.");
            i++;
        }

        return tokens;
    }

    private static (string Value, int Next) ReadNameOrIri(string text, int i, DiagnosticBag diagnostics)
    {
        if(i < text.Length && text[i] == '<')
        {
            int end = text.IndexOf('>', i + 1);
            if(end < 0)
            {
                Report(diagnostics, "Unterminated datatype IRI.");

                return (string.Empty, text.Length);
            }

            return ("<" + text[(i + 1)..end], end + 1);
        }

        int start = i;
        while(i < text.Length && (IsNameChar(text[i]) || text[i] == ':'))
        {
            i++;
        }

        return (text[start..i], i);
    }

    private static bool IsNameStartChar(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    private static bool IsNameChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
    }

    //S-expression tree.

    private sealed class SExpr
    {
        /// <summary>The constructor name for a headed group, <c>null</c> for a bare parenthesis group.</summary>
        public string? Head { get; init; }

        /// <summary>The atom token for a leaf, default for a group.</summary>
        public Token Atom { get; init; }

        /// <summary>Whether this node is an atom.</summary>
        public bool IsAtom { get; init; }

        /// <summary>The group's children, in document order.</summary>
        public List<SExpr> Children { get; } = [];
    }

    private static SExpr BuildTree(List<Token> tokens, DiagnosticBag diagnostics)
    {
        SExpr root = new() { Head = null };
        Stack<SExpr> open = new();
        open.Push(root);

        int i = 0;
        while(i < tokens.Count)
        {
            Token token = tokens[i];

            if(token.Kind == TokenKind.Name && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Open)
            {
                SExpr group = new() { Head = token.Text };
                open.Peek().Children.Add(group);
                open.Push(group);
                i += 2;

                continue;
            }

            if(token.Kind == TokenKind.Open)
            {
                SExpr group = new() { Head = null };
                open.Peek().Children.Add(group);
                open.Push(group);
                i++;

                continue;
            }

            if(token.Kind == TokenKind.Close)
            {
                if(open.Count == 1)
                {
                    Report(diagnostics, "Unbalanced ')'.");
                }
                else
                {
                    open.Pop();
                }

                i++;

                continue;
            }

            open.Peek().Children.Add(new SExpr { IsAtom = true, Atom = token });
            i++;
        }

        if(open.Count != 1)
        {
            Report(diagnostics, "Unbalanced '(' at end of document.");
        }

        return root;
    }

    private static void Report(DiagnosticBag diagnostics, string message)
    {
        diagnostics.Add(new Diagnostic(
            WellKnownDiagnostics.Owl.MalformedAxiomStructure,
            DiagnosticSeverity.Error,
            default(SourceSpan),
            Utf8String.FromString(message)));
    }

    //Conversion.

    //A declaration constructor's payload: the entity kind plus the IRI.
    private sealed record DeclarationValue(OwlEntityKind Kind, Utf8String Iri);


    private sealed class ReaderContext
    {
        public DiagnosticBag Diagnostics { get; } = new();

        public ImmutableArray<OwlAxiom>.Builder Axioms { get; } = ImmutableArray.CreateBuilder<OwlAxiom>();

        public NamedNode? OntologyIri { get; private set; }

        public HashSet<Utf8String> DeclaredClasses { get; } = [];

        public HashSet<Utf8String> DeclaredObjectProperties { get; } = [];

        public HashSet<Utf8String> DeclaredDataProperties { get; } = [];

        public HashSet<Utf8String> DeclaredAnnotationProperties { get; } = [];

        public HashSet<Utf8String> DeclaredDatatypes { get; } = [];

        /// <summary>The prefix table the document's prefixed names resolve through.</summary>
        private Dictionary<string, string> Prefixes { get; } = [];

        /// <summary>Converted values per tree node, filled in post-order.</summary>
        private Dictionary<SExpr, object?> Converted { get; } = [];

        /// <summary>The synthetic origin every functional-syntax axiom carries — the syntax has no triples to anchor to.</summary>
        private Quad Origin { get; set; } = new(
            new NamedNode(Utf8String.FromString("urn:veritas:functional-syntax")),
            new NamedNode(Utf8String.FromString("urn:veritas:functional-syntax")),
            new NamedNode(Utf8String.FromString("urn:veritas:functional-syntax")),
            Graph: null);

        /// <summary>Converts the document tree: prefixes, the ontology header, and the axioms.</summary>
        public void ConvertDocument(SExpr root)
        {
            foreach(SExpr top in root.Children)
            {
                if(top.Head == "Prefix")
                {
                    //Prefix(p:=<iri>) tokenizes as Name(p:) Equals Iri.
                    if(top.Children.Count >= 3
                        && top.Children[0] is { IsAtom: true, Atom: { Kind: TokenKind.Name } prefixToken }
                        && top.Children[2] is { IsAtom: true, Atom: { Kind: TokenKind.Iri } iriToken })
                    {
                        Prefixes[prefixToken.Text.TrimEnd(':')] = iriToken.Text;
                    }

                    continue;
                }

                if(top.Head == "Ontology")
                {
                    ConvertOntology(top);
                }
            }
        }

        private void ConvertOntology(SExpr ontology)
        {
            bool headerDone = false;
            foreach(SExpr child in ontology.Children)
            {
                //The leading bare IRIs are the ontology IRI and version IRI.
                if(!headerDone && child.IsAtom && child.Atom.Kind == TokenKind.Iri)
                {
                    OntologyIri ??= new NamedNode(Utf8String.FromString(child.Atom.Text));

                    continue;
                }

                headerDone = true;

                if(child.Head == "Import" && child.Children.Count == 1 && child.Children[0].Atom.Kind == TokenKind.Iri)
                {
                    Axioms.Add(new OwlImportAxiom(new NamedNode(Utf8String.FromString(child.Children[0].Atom.Text))) { Origin = Origin });

                    continue;
                }

                if(child.Head == "Annotation")
                {
                    //An ontology annotation surfaces as an annotation
                    //assertion on the ontology IRI — the same triple the RDF
                    //mapping carries it as. An anonymous ontology has no
                    //subject to carry it, so the annotation is consumed.
                    ConvertSubtree(child);
                    if(OntologyIri is NamedNode ontologyIri && Converted[child] is OwlAnnotation ontologyAnnotation)
                    {
                        Axioms.Add(new OwlAnnotationAssertionAxiom(ontologyIri, ontologyAnnotation.Property, ontologyAnnotation.Value)
                        {
                            Origin = Origin,
                            Annotations = ontologyAnnotation.Annotations
                        });
                    }

                    continue;
                }

                if(child.Head is null)
                {
                    Diagnostics.Add(new Diagnostic(
                        WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                        DiagnosticSeverity.Error,
                        default(SourceSpan),
                        Utf8String.FromString("A bare group is not an axiom.")));

                    continue;
                }

                ConvertSubtree(child);
                ConvertAxiom(child);
            }
        }

        //Converts a constructor subtree in post-order over an explicit
        //stack: a group constructs once every child has its value.
        private void ConvertSubtree(SExpr root)
        {
            Stack<SExpr> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                SExpr node = work.Peek();
                if(Converted.ContainsKey(node))
                {
                    work.Pop();

                    continue;
                }

                if(node.IsAtom)
                {
                    Converted[node] = ConvertAtom(node.Atom);
                    work.Pop();

                    continue;
                }

                bool ready = true;
                foreach(SExpr child in node.Children)
                {
                    if(!Converted.ContainsKey(child))
                    {
                        ready = false;
                        work.Push(child);
                    }
                }

                if(!ready)
                {
                    continue;
                }

                Converted[node] = ConstructValue(node);
                work.Pop();
            }
        }

        private object? ConvertAtom(Token token)
        {
            return token.Kind switch
            {
                TokenKind.Iri => Utf8String.FromString(token.Text),
                TokenKind.Name => ResolveName(token.Text) is Utf8String resolvedName ? resolvedName : null,
                TokenKind.BlankNode => new BlankNode(Utf8String.FromString(token.Text)),
                TokenKind.Number => int.Parse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture),
                TokenKind.Literal => MakeLiteral(token),
                _ => null
            };
        }

        private Utf8String? ResolveName(string name)
        {
            int colon = name.IndexOf(':', StringComparison.Ordinal);
            if(colon < 0)
            {
                //A bare name in atom position is a constructor misuse; report it.
                Diagnostics.Add(new Diagnostic(
                    WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                    DiagnosticSeverity.Error,
                    default(SourceSpan),
                    Utf8String.FromString($"Unresolvable name '{name}'.")));

                return null;
            }

            string prefix = name[..colon];
            string local = name[(colon + 1)..];

            if(Prefixes.TryGetValue(prefix, out string? expansion))
            {
                return Utf8String.FromString(expansion + local);
            }

            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                DiagnosticSeverity.Error,
                default(SourceSpan),
                Utf8String.FromString($"Undeclared prefix '{prefix}:'.")));

            return null;
        }

        private Literal MakeLiteral(Token token)
        {
            if(token.LiteralLanguage is string language)
            {
                return new Literal(Utf8String.FromString(token.Text), new NamedNode(Vocabulary.Rdf.LangString), Utf8String.FromString(language));
            }

            Utf8String datatype = token.LiteralDatatype switch
            {
                null => Vocabulary.Xsd.String,
                ['<', .. var full] => Utf8String.FromString(full),
                _ => ResolveName(token.LiteralDatatype) is Utf8String resolved ? resolved : Vocabulary.Xsd.String
            };

            return new Literal(Utf8String.FromString(token.Text), new NamedNode(datatype));
        }

        //Constructs the value of a headed (or bare) group from its
        //already-converted children.
        private object? ConstructValue(SExpr node)
        {
            List<object?> children = new(node.Children.Count);
            foreach(SExpr child in node.Children)
            {
                children.Add(Converted[child]);
            }

            switch(node.Head)
            {
                case null:
                {
                    return children;
                }
                case "Annotation":
                {
                    //An annotation frame: optional nested annotations, then
                    //the annotation property and the value.
                    ImmutableArray<OwlAnnotation>.Builder? nested = null;
                    List<object?> parts = new(children.Count);
                    foreach(object? childValue in children)
                    {
                        if(childValue is OwlAnnotation nestedAnnotation)
                        {
                            nested ??= ImmutableArray.CreateBuilder<OwlAnnotation>();
                            nested.Add(nestedAnnotation);
                        }
                        else
                        {
                            parts.Add(childValue);
                        }
                    }

                    return parts is [Utf8String annotationProperty, var annotationValue]
                        ? new OwlAnnotation(new NamedNode(annotationProperty), AsAnnotationValue(annotationValue)) { Annotations = nested?.ToImmutable() ?? [] }
                        : null;
                }
                case "Class" when children is [Utf8String classIri]:
                {
                    return new DeclarationValue(OwlEntityKind.Class, classIri);
                }
                case "Datatype" when children is [Utf8String datatypeIri]:
                {
                    return new DeclarationValue(OwlEntityKind.Datatype, datatypeIri);
                }
                case "ObjectProperty" when children is [Utf8String objectPropertyIri]:
                {
                    return new DeclarationValue(OwlEntityKind.ObjectProperty, objectPropertyIri);
                }
                case "DataProperty" when children is [Utf8String dataPropertyIri]:
                {
                    return new DeclarationValue(OwlEntityKind.DataProperty, dataPropertyIri);
                }
                case "AnnotationProperty" when children is [Utf8String annotationPropertyIri]:
                {
                    return new DeclarationValue(OwlEntityKind.AnnotationProperty, annotationPropertyIri);
                }
                case "NamedIndividual" when children is [Utf8String individualIri]:
                {
                    return new DeclarationValue(OwlEntityKind.NamedIndividual, individualIri);
                }
                case "ObjectInverseOf" when AsObjectProperty(children[0]) is OwlObjectPropertyReference inverted:
                {
                    return new OwlInverseObjectProperty(inverted.Named);
                }
                case "ObjectIntersectionOf":
                {
                    return new OwlObjectIntersectionOf(AsClassList(children));
                }
                case "ObjectUnionOf":
                {
                    return new OwlObjectUnionOf(AsClassList(children));
                }
                case "ObjectComplementOf":
                {
                    return new OwlObjectComplementOf(AsClass(children[0]));
                }
                case "ObjectOneOf":
                {
                    return new OwlObjectOneOf(AsIndividualList(children));
                }
                case "ObjectSomeValuesFrom":
                {
                    return new OwlObjectSomeValuesFrom(AsObjectProperty(children[0]), AsClass(children[1]));
                }
                case "ObjectAllValuesFrom":
                {
                    return new OwlObjectAllValuesFrom(AsObjectProperty(children[0]), AsClass(children[1]));
                }
                case "ObjectHasValue":
                {
                    return new OwlObjectHasValue(AsObjectProperty(children[0]), AsIndividual(children[1]));
                }
                case "ObjectHasSelf":
                {
                    return new OwlObjectHasSelf(AsObjectProperty(children[0]));
                }
                case "ObjectMinCardinality" or "ObjectMaxCardinality" or "ObjectExactCardinality":
                {
                    OwlCardinalityKind kind = node.Head switch
                    {
                        "ObjectMinCardinality" => OwlCardinalityKind.Min,
                        "ObjectMaxCardinality" => OwlCardinalityKind.Max,
                        _ => OwlCardinalityKind.Exact
                    };

                    return new OwlObjectCardinality(
                        kind,
                        children[0] is int bound ? bound : 0,
                        AsObjectProperty(children[1]),
                        children.Count > 2 ? AsClass(children[2]) : null);
                }
                case "DataSomeValuesFrom":
                {
                    return new OwlDataSomeValuesFrom([AsDataProperty(children[0])], AsDataRange(children[1]));
                }
                case "DataAllValuesFrom":
                {
                    return new OwlDataAllValuesFrom([AsDataProperty(children[0])], AsDataRange(children[1]));
                }
                case "DataHasValue" when children[1] is Literal dataValue:
                {
                    return new OwlDataHasValue(AsDataProperty(children[0]), dataValue);
                }
                case "DataMinCardinality" or "DataMaxCardinality" or "DataExactCardinality":
                {
                    OwlCardinalityKind kind = node.Head switch
                    {
                        "DataMinCardinality" => OwlCardinalityKind.Min,
                        "DataMaxCardinality" => OwlCardinalityKind.Max,
                        _ => OwlCardinalityKind.Exact
                    };

                    return new OwlDataCardinality(
                        kind,
                        children[0] is int bound ? bound : 0,
                        AsDataProperty(children[1]),
                        children.Count > 2 ? AsDataRange(children[2]) : null);
                }
                case "DataIntersectionOf":
                {
                    return new OwlDataIntersectionOf(AsDataRangeList(children));
                }
                case "DataUnionOf":
                {
                    return new OwlDataUnionOf(AsDataRangeList(children));
                }
                case "DataComplementOf":
                {
                    return new OwlDataComplementOf(AsDataRange(children[0]));
                }
                case "DataOneOf":
                {
                    List<Literal> literals = [];
                    foreach(object? child in children)
                    {
                        if(child is Literal literal)
                        {
                            literals.Add(literal);
                        }
                    }

                    return new OwlDataOneOf(literals);
                }
                case "DatatypeRestriction":
                {
                    List<OwlFacetRestriction> facets = [];
                    for(int i = 1; i + 1 < children.Count; i += 2)
                    {
                        if(children[i] is Utf8String facet && children[i + 1] is Literal value)
                        {
                            facets.Add(new OwlFacetRestriction(new NamedNode(facet), value));
                        }
                    }

                    return new OwlDatatypeRestriction(new NamedNode(AsIri(children[0])), facets);
                }
                case "ObjectPropertyChain":
                {
                    List<OwlObjectPropertyExpression> links = [];
                    foreach(object? child in children)
                    {
                        links.Add(AsObjectProperty(child));
                    }

                    return links;
                }
                default:
                {
                    //Axiom heads construct in ConvertAxiom; leave the
                    //converted children accessible through the group node.
                    return children;
                }
            }
        }

        //Constructs the axiom for a top-level constructor group whose
        //subtree is converted.
        private void ConvertAxiom(SExpr node)
        {
            if(Converted[node] is DeclarationValue || node.Head is null)
            {
                //A bare Class(:A) at axiom position would be malformed; the
                //Declaration head handles the well-formed case below.
                return;
            }

            List<object?> raw = Converted[node] is List<object?> converted ? converted : [Converted[node]];

            //Axiom-frame annotations precede the logical arguments; they
            //attach to every axiom the frame produces (a pairwise expansion
            //carries the frame's annotations on each pair).
            ImmutableArray<OwlAnnotation>.Builder? annotations = null;
            List<object?> children = new(raw.Count);
            foreach(object? value in raw)
            {
                if(value is OwlAnnotation annotation)
                {
                    annotations ??= ImmutableArray.CreateBuilder<OwlAnnotation>();
                    annotations.Add(annotation);
                }
                else
                {
                    children.Add(value);
                }
            }

            int firstAxiom = Axioms.Count;

            switch(node.Head)
            {
                case "Declaration" when children is [DeclarationValue declaration]:
                {
                    AddDeclaration(declaration);
                    break;
                }
                case "SubClassOf" when children.Count == 2:
                {
                    Axioms.Add(new OwlSubClassOfAxiom(AsClass(children[0]), AsClass(children[1])) { Origin = Origin });
                    break;
                }
                case "EquivalentClasses":
                {
                    ForEachPair(children, (first, second) =>
                        Axioms.Add(new OwlEquivalentClassesAxiom(AsClass(first), AsClass(second)) { Origin = Origin }));
                    break;
                }
                case "DisjointClasses":
                {
                    Axioms.Add(new OwlDisjointClassesAxiom(AsClassList(children)) { Origin = Origin });
                    break;
                }
                case "DisjointUnion" when children.Count >= 2 && children[0] is Utf8String unionClass:
                {
                    Axioms.Add(new OwlDisjointUnionAxiom(new NamedNode(unionClass), AsClassList(children[1..])) { Origin = Origin });
                    break;
                }
                case "SubObjectPropertyOf" when children.Count == 2:
                {
                    if(children[0] is List<OwlObjectPropertyExpression> chain)
                    {
                        Axioms.Add(new OwlPropertyChainAxiom(chain, AsObjectProperty(children[1])) { Origin = Origin });
                    }
                    else
                    {
                        Axioms.Add(new OwlSubObjectPropertyOfAxiom(AsObjectProperty(children[0]), AsObjectProperty(children[1])) { Origin = Origin });
                    }

                    break;
                }
                case "EquivalentObjectProperties":
                {
                    ForEachPair(children, (first, second) =>
                        Axioms.Add(new OwlEquivalentObjectPropertiesAxiom(AsObjectProperty(first), AsObjectProperty(second)) { Origin = Origin }));
                    break;
                }
                case "DisjointObjectProperties":
                {
                    List<OwlObjectPropertyExpression> properties = [];
                    foreach(object? child in children)
                    {
                        properties.Add(AsObjectProperty(child));
                    }

                    Axioms.Add(new OwlDisjointObjectPropertiesAxiom(properties) { Origin = Origin });
                    break;
                }
                case "InverseObjectProperties" when children.Count == 2:
                {
                    Axioms.Add(new OwlInverseObjectPropertiesAxiom(AsObjectProperty(children[0]), AsObjectProperty(children[1])) { Origin = Origin });
                    break;
                }
                case "ObjectPropertyDomain" when children.Count == 2:
                {
                    Axioms.Add(new OwlObjectPropertyDomainAxiom(AsObjectProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                    break;
                }
                case "ObjectPropertyRange" when children.Count == 2:
                {
                    Axioms.Add(new OwlObjectPropertyRangeAxiom(AsObjectProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                    break;
                }
                case "FunctionalObjectProperty" or "InverseFunctionalObjectProperty" or "TransitiveObjectProperty"
                    or "SymmetricObjectProperty" or "AsymmetricObjectProperty" or "ReflexiveObjectProperty" or "IrreflexiveObjectProperty":
                {
                    OwlPropertyCharacteristic characteristic = node.Head switch
                    {
                        "FunctionalObjectProperty" => OwlPropertyCharacteristic.Functional,
                        "InverseFunctionalObjectProperty" => OwlPropertyCharacteristic.InverseFunctional,
                        "TransitiveObjectProperty" => OwlPropertyCharacteristic.Transitive,
                        "SymmetricObjectProperty" => OwlPropertyCharacteristic.Symmetric,
                        "AsymmetricObjectProperty" => OwlPropertyCharacteristic.Asymmetric,
                        "ReflexiveObjectProperty" => OwlPropertyCharacteristic.Reflexive,
                        _ => OwlPropertyCharacteristic.Irreflexive
                    };

                    Axioms.Add(new OwlObjectPropertyCharacteristicAxiom(characteristic, AsObjectProperty(children[0])) { Origin = Origin });
                    break;
                }
                case "SubDataPropertyOf" when children.Count == 2:
                {
                    Axioms.Add(new OwlSubDataPropertyOfAxiom(AsDataProperty(children[0]), AsDataProperty(children[1])) { Origin = Origin });
                    break;
                }
                case "EquivalentDataProperties":
                {
                    ForEachPair(children, (first, second) =>
                        Axioms.Add(new OwlEquivalentDataPropertiesAxiom(AsDataProperty(first), AsDataProperty(second)) { Origin = Origin }));
                    break;
                }
                case "DisjointDataProperties":
                {
                    List<NamedNode> properties = [];
                    foreach(object? child in children)
                    {
                        properties.Add(AsDataProperty(child));
                    }

                    Axioms.Add(new OwlDisjointDataPropertiesAxiom(properties) { Origin = Origin });
                    break;
                }
                case "DataPropertyDomain" when children.Count == 2:
                {
                    Axioms.Add(new OwlDataPropertyDomainAxiom(AsDataProperty(children[0]), AsClass(children[1])) { Origin = Origin });
                    break;
                }
                case "DataPropertyRange" when children.Count == 2:
                {
                    Axioms.Add(new OwlDataPropertyRangeAxiom(AsDataProperty(children[0]), AsDataRange(children[1])) { Origin = Origin });
                    break;
                }
                case "FunctionalDataProperty":
                {
                    Axioms.Add(new OwlFunctionalDataPropertyAxiom(AsDataProperty(children[0])) { Origin = Origin });
                    break;
                }
                case "DatatypeDefinition" when children.Count == 2:
                {
                    Axioms.Add(new OwlDatatypeDefinitionAxiom(new NamedNode(AsIri(children[0])), AsDataRange(children[1])) { Origin = Origin });
                    break;
                }
                case "HasKey" when children.Count >= 1:
                {
                    //HasKey(CE (opes…) (dpes…)): the bare groups carry the
                    //key components. An IRI key with no declaration basis
                    //counts as an object property.
                    List<OwlObjectPropertyExpression> objectKeys = [];
                    List<NamedNode> dataKeys = [];
                    for(int i = 1; i < children.Count; i++)
                    {
                        if(children[i] is not List<object?> group)
                        {
                            continue;
                        }

                        foreach(object? key in group)
                        {
                            if(key is Utf8String keyIri && DeclaredDataProperties.Contains(keyIri))
                            {
                                dataKeys.Add(new NamedNode(keyIri));
                            }
                            else
                            {
                                objectKeys.Add(AsObjectProperty(key));
                            }
                        }
                    }

                    Axioms.Add(new OwlHasKeyAxiom(AsClass(children[0]), objectKeys, dataKeys) { Origin = Origin });
                    break;
                }
                case "SameIndividual":
                {
                    ForEachPair(children, (first, second) =>
                        Axioms.Add(new OwlSameIndividualAxiom(AsIndividual(first), AsIndividual(second)) { Origin = Origin }));
                    break;
                }
                case "DifferentIndividuals":
                {
                    Axioms.Add(new OwlDifferentIndividualsAxiom(AsIndividualList(children)) { Origin = Origin });
                    break;
                }
                case "ClassAssertion" when children.Count == 2:
                {
                    Axioms.Add(new OwlClassAssertionAxiom(AsClass(children[0]), AsIndividual(children[1])) { Origin = Origin });
                    break;
                }
                case "ObjectPropertyAssertion" when children.Count == 3 && AsObjectProperty(children[0]) is OwlObjectPropertyReference assertedProperty:
                {
                    Axioms.Add(new OwlObjectPropertyAssertionAxiom(AsIndividual(children[1]), assertedProperty.Named, AsIndividual(children[2])) { Origin = Origin });
                    break;
                }
                case "NegativeObjectPropertyAssertion" when children.Count == 3:
                {
                    Axioms.Add(new OwlNegativeObjectPropertyAssertionAxiom(AsIndividual(children[1]), AsObjectProperty(children[0]), AsIndividual(children[2])) { Origin = Origin });
                    break;
                }
                case "DataPropertyAssertion" when children.Count == 3 && children[2] is Literal dataValue:
                {
                    Axioms.Add(new OwlDataPropertyAssertionAxiom(AsIndividual(children[1]), AsDataProperty(children[0]), dataValue) { Origin = Origin });
                    break;
                }
                case "NegativeDataPropertyAssertion" when children.Count == 3 && children[2] is Literal negativeValue:
                {
                    Axioms.Add(new OwlNegativeDataPropertyAssertionAxiom(AsIndividual(children[1]), AsDataProperty(children[0]), negativeValue) { Origin = Origin });
                    break;
                }
                case "AnnotationAssertion" when children.Count == 3:
                {
                    Axioms.Add(new OwlAnnotationAssertionAxiom(AsIndividual(children[1]), new NamedNode(AsIri(children[0])), AsAnnotationValue(children[2])) { Origin = Origin });
                    break;
                }
                case "SubAnnotationPropertyOf" when children.Count == 2:
                {
                    Axioms.Add(new OwlSubAnnotationPropertyOfAxiom(new NamedNode(AsIri(children[0])), new NamedNode(AsIri(children[1]))) { Origin = Origin });
                    break;
                }
                case "AnnotationPropertyDomain" when children.Count == 2:
                {
                    Axioms.Add(new OwlAnnotationPropertyDomainAxiom(new NamedNode(AsIri(children[0])), new NamedNode(AsIri(children[1]))) { Origin = Origin });
                    break;
                }
                case "AnnotationPropertyRange" when children.Count == 2:
                {
                    Axioms.Add(new OwlAnnotationPropertyRangeAxiom(new NamedNode(AsIri(children[0])), new NamedNode(AsIri(children[1]))) { Origin = Origin });
                    break;
                }
                default:
                {
                    Diagnostics.Add(new Diagnostic(
                        WellKnownDiagnostics.Owl.UnsupportedConstruct,
                        DiagnosticSeverity.Error,
                        default(SourceSpan),
                        Utf8String.FromString($"Unrecognised axiom constructor '{node.Head}'.")));
                    break;
                }
            }

            if(annotations is not null)
            {
                ImmutableArray<OwlAnnotation> attached = annotations.ToImmutable();
                for(int i = firstAxiom; i < Axioms.Count; i++)
                {
                    Axioms[i] = Axioms[i] with { Annotations = attached };
                }
            }
        }

        private void AddDeclaration(DeclarationValue declaration)
        {
            Axioms.Add(new OwlDeclarationAxiom(declaration.Kind, new NamedNode(declaration.Iri)) { Origin = Origin });

            HashSet<Utf8String> target = declaration.Kind switch
            {
                OwlEntityKind.Class => DeclaredClasses,
                OwlEntityKind.Datatype => DeclaredDatatypes,
                OwlEntityKind.ObjectProperty => DeclaredObjectProperties,
                OwlEntityKind.DataProperty => DeclaredDataProperties,
                OwlEntityKind.AnnotationProperty => DeclaredAnnotationProperties,
                _ => DeclaredClasses
            };

            if(declaration.Kind != OwlEntityKind.NamedIndividual)
            {
                target.Add(declaration.Iri);
            }
        }

        private static void ForEachPair(List<object?> values, Action<object?, object?> emit)
        {
            for(int i = 0; i < values.Count; i++)
            {
                for(int j = i + 1; j < values.Count; j++)
                {
                    emit(values[i], values[j]);
                }
            }
        }

        //Coercions: atoms resolve per the requesting context.

        private Utf8String AsIri(object? value)
        {
            if(value is Utf8String iri)
            {
                return iri;
            }

            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                DiagnosticSeverity.Error,
                default(SourceSpan),
                Utf8String.FromString("An IRI was expected.")));

            return Utf8String.FromString("urn:veritas:invalid");
        }

        private OwlClassExpression AsClass(object? value)
        {
            return value switch
            {
                OwlClassExpression expression => expression,
                Utf8String iri => new OwlClassReference(new NamedNode(iri)),
                _ => Invalid()
            };

            OwlClassExpression Invalid()
            {
                Diagnostics.Add(new Diagnostic(
                    WellKnownDiagnostics.Owl.MalformedClassExpression,
                    DiagnosticSeverity.Error,
                    default(SourceSpan),
                    Utf8String.FromString("A class expression was expected.")));

                return new OwlClassReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")));
            }
        }

        private List<OwlClassExpression> AsClassList(List<object?> values)
        {
            List<OwlClassExpression> expressions = new(values.Count);
            foreach(object? value in values)
            {
                expressions.Add(AsClass(value));
            }

            return expressions;
        }

        private List<OwlDataRange> AsDataRangeList(List<object?> values)
        {
            List<OwlDataRange> ranges = new(values.Count);
            foreach(object? value in values)
            {
                ranges.Add(AsDataRange(value));
            }

            return ranges;
        }

        private OwlObjectPropertyExpression AsObjectProperty(object? value)
        {
            return value switch
            {
                OwlObjectPropertyExpression expression => expression,
                Utf8String iri => new OwlObjectPropertyReference(new NamedNode(iri)),
                _ => Invalid()
            };

            OwlObjectPropertyExpression Invalid()
            {
                Diagnostics.Add(new Diagnostic(
                    WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                    DiagnosticSeverity.Error,
                    default(SourceSpan),
                    Utf8String.FromString("An object property expression was expected.")));

                return new OwlObjectPropertyReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")));
            }
        }

        private NamedNode AsDataProperty(object? value)
        {
            return new NamedNode(AsIri(value));
        }

        private OwlDataRange AsDataRange(object? value)
        {
            return value switch
            {
                OwlDataRange range => range,
                Utf8String iri => new OwlDatatypeReference(new NamedNode(iri)),
                _ => Invalid()
            };

            OwlDataRange Invalid()
            {
                Diagnostics.Add(new Diagnostic(
                    WellKnownDiagnostics.Owl.MalformedClassExpression,
                    DiagnosticSeverity.Error,
                    default(SourceSpan),
                    Utf8String.FromString("A data range was expected.")));

                return new OwlDatatypeReference(new NamedNode(Utf8String.FromString("urn:veritas:invalid")));
            }
        }

        private RdfTerm AsIndividual(object? value)
        {
            return value switch
            {
                BlankNode blank => blank,
                Utf8String iri => new NamedNode(iri),
                _ => InvalidIndividual()
            };

            RdfTerm InvalidIndividual()
            {
                Diagnostics.Add(new Diagnostic(
                    WellKnownDiagnostics.Owl.MalformedAxiomStructure,
                    DiagnosticSeverity.Error,
                    default(SourceSpan),
                    Utf8String.FromString("An individual was expected.")));

                return new NamedNode(Utf8String.FromString("urn:veritas:invalid"));
            }
        }

        private RdfTerm AsAnnotationValue(object? value)
        {
            return value switch
            {
                Literal literal => literal,
                BlankNode blank => blank,
                Utf8String iri => new NamedNode(iri),
                _ => AsIndividual(value)
            };
        }

        private List<RdfTerm> AsIndividualList(List<object?> values)
        {
            List<RdfTerm> individuals = new(values.Count);
            foreach(object? value in values)
            {
                individuals.Add(AsIndividual(value));
            }

            return individuals;
        }
    }
}
