using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Structural;

/// <summary>
/// An OWL 2 class expression: a named class or one of the class-expression
/// constructors of the structural specification — boolean connectives,
/// enumeration, and the object and data property restrictions.
/// </summary>
public abstract record OwlClassExpression
{
    /// <summary>
    /// Appends this expression's direct individual-position terms to
    /// <paramref name="individualsToAppendTo"/> and pushes its direct child
    /// class expressions onto <paramref name="expressionsToAppendTo"/>.
    /// Individual positions are the value individual of an
    /// <see cref="OwlObjectHasValue"/> and the enumerated members of an
    /// <see cref="OwlObjectOneOf"/>; literals are not individuals. The member
    /// touches only this expression's own direct terms and never descends: a
    /// caller reaches the individuals nested inside the pushed child
    /// expressions by draining the worklist, invoking this member on each
    /// popped expression until the worklist empties.
    /// </summary>
    /// <param name="individualsToAppendTo">The list this expression's direct individual-position terms are appended to.</param>
    /// <param name="expressionsToAppendTo">The worklist this expression's direct child class expressions are pushed onto for a caller to drain.</param>
    public abstract void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo);
}

/// <summary>The cardinality flavour of a cardinality restriction.</summary>
public enum OwlCardinalityKind
{
    /// <summary><c>ObjectMinCardinality</c> / <c>DataMinCardinality</c>.</summary>
    Min = 0,

    /// <summary><c>ObjectMaxCardinality</c> / <c>DataMaxCardinality</c>.</summary>
    Max = 1,

    /// <summary><c>ObjectExactCardinality</c> / <c>DataExactCardinality</c>.</summary>
    Exact = 2,
}

/// <summary>A named class used directly (including <c>owl:Thing</c> and <c>owl:Nothing</c>).</summary>
/// <param name="Class">The class IRI node.</param>
[DebuggerDisplay("Class {Class}")]
public sealed record OwlClassReference(NamedNode Class): OwlClassExpression
{
    /// <summary>Appends nothing and pushes nothing: a named class mentions no individual and has no child expression.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>The intersection of class expressions (<c>ObjectIntersectionOf</c>).</summary>
/// <param name="Operands">The operand expressions, in declaration order.</param>
[DebuggerDisplay("ObjectIntersectionOf ({Operands.Count})")]
public sealed record OwlObjectIntersectionOf(IReadOnlyList<OwlClassExpression> Operands): OwlClassExpression
{
    /// <summary>Pushes each operand expression; an intersection mentions no direct individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        foreach(OwlClassExpression operand in Operands)
        {
            expressionsToAppendTo.Push(operand);
        }
    }
}

/// <summary>The union of class expressions (<c>ObjectUnionOf</c>).</summary>
/// <param name="Operands">The operand expressions, in declaration order.</param>
[DebuggerDisplay("ObjectUnionOf ({Operands.Count})")]
public sealed record OwlObjectUnionOf(IReadOnlyList<OwlClassExpression> Operands): OwlClassExpression
{
    /// <summary>Pushes each operand expression; a union mentions no direct individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        foreach(OwlClassExpression operand in Operands)
        {
            expressionsToAppendTo.Push(operand);
        }
    }
}

/// <summary>The complement of a class expression (<c>ObjectComplementOf</c>).</summary>
/// <param name="Operand">The complemented expression.</param>
[DebuggerDisplay("ObjectComplementOf")]
public sealed record OwlObjectComplementOf(OwlClassExpression Operand): OwlClassExpression
{
    /// <summary>Pushes the complemented expression; a complement mentions no direct individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(Operand);
    }
}

/// <summary>An enumeration of individuals (<c>ObjectOneOf</c>).</summary>
/// <param name="Individuals">The enumerated individuals (named or anonymous), in declaration order.</param>
[DebuggerDisplay("ObjectOneOf ({Individuals.Count})")]
public sealed record OwlObjectOneOf(IReadOnlyList<RdfTerm> Individuals): OwlClassExpression
{
    /// <summary>Appends every enumerated individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.AddRange(Individuals);
    }
}

/// <summary>An existential object restriction (<c>ObjectSomeValuesFrom</c>).</summary>
/// <param name="Property">The restricted property expression.</param>
/// <param name="Filler">The filler expression.</param>
[DebuggerDisplay("ObjectSomeValuesFrom")]
public sealed record OwlObjectSomeValuesFrom(OwlObjectPropertyExpression Property, OwlClassExpression Filler): OwlClassExpression
{
    /// <summary>Pushes the filler expression; the property is a property expression and mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(Filler);
    }
}

/// <summary>A universal object restriction (<c>ObjectAllValuesFrom</c>).</summary>
/// <param name="Property">The restricted property expression.</param>
/// <param name="Filler">The filler expression.</param>
[DebuggerDisplay("ObjectAllValuesFrom")]
public sealed record OwlObjectAllValuesFrom(OwlObjectPropertyExpression Property, OwlClassExpression Filler): OwlClassExpression
{
    /// <summary>Pushes the filler expression; the property is a property expression and mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        expressionsToAppendTo.Push(Filler);
    }
}

/// <summary>An individual-value restriction (<c>ObjectHasValue</c>).</summary>
/// <param name="Property">The restricted property expression.</param>
/// <param name="Individual">The required value individual.</param>
[DebuggerDisplay("ObjectHasValue")]
public sealed record OwlObjectHasValue(OwlObjectPropertyExpression Property, RdfTerm Individual): OwlClassExpression
{
    /// <summary>Appends the required value individual; the property is a property expression and mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        individualsToAppendTo.Add(Individual);
    }
}

/// <summary>A local-reflexivity restriction (<c>ObjectHasSelf</c>).</summary>
/// <param name="Property">The restricted property expression.</param>
[DebuggerDisplay("ObjectHasSelf")]
public sealed record OwlObjectHasSelf(OwlObjectPropertyExpression Property): OwlClassExpression
{
    /// <summary>Appends nothing and pushes nothing: a local-reflexivity restriction holds only a property expression.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>An object cardinality restriction (<c>ObjectMinCardinality</c> / <c>ObjectMaxCardinality</c> / <c>ObjectExactCardinality</c>), qualified when <see cref="Filler"/> is present.</summary>
/// <param name="Kind">The cardinality flavour.</param>
/// <param name="Cardinality">The non-negative bound.</param>
/// <param name="Property">The restricted property expression.</param>
/// <param name="Filler">The qualifying filler, or <c>null</c> for an unqualified restriction.</param>
[DebuggerDisplay("Object{Kind}Cardinality {Cardinality}")]
public sealed record OwlObjectCardinality(OwlCardinalityKind Kind, int Cardinality, OwlObjectPropertyExpression Property, OwlClassExpression? Filler): OwlClassExpression
{
    /// <summary>Pushes the qualifying filler expression when one is present; the property mentions no individual.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
        if(Filler is OwlClassExpression filler)
        {
            expressionsToAppendTo.Push(filler);
        }
    }
}

/// <summary>An existential data restriction (<c>DataSomeValuesFrom</c>).</summary>
/// <param name="Properties">The restricted data properties (one, or several for the n-ary form).</param>
/// <param name="Range">The filler range.</param>
[DebuggerDisplay("DataSomeValuesFrom")]
public sealed record OwlDataSomeValuesFrom(IReadOnlyList<NamedNode> Properties, OwlDataRange Range): OwlClassExpression
{
    /// <summary>Appends nothing and pushes nothing: a data existential holds data properties and a data range.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A universal data restriction (<c>DataAllValuesFrom</c>).</summary>
/// <param name="Properties">The restricted data properties (one, or several for the n-ary form).</param>
/// <param name="Range">The filler range.</param>
[DebuggerDisplay("DataAllValuesFrom")]
public sealed record OwlDataAllValuesFrom(IReadOnlyList<NamedNode> Properties, OwlDataRange Range): OwlClassExpression
{
    /// <summary>Appends nothing and pushes nothing: a data universal holds data properties and a data range.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A literal-value restriction (<c>DataHasValue</c>).</summary>
/// <param name="Property">The restricted data property.</param>
/// <param name="Value">The required literal value.</param>
[DebuggerDisplay("DataHasValue")]
public sealed record OwlDataHasValue(NamedNode Property, Literal Value): OwlClassExpression
{
    /// <summary>Appends nothing and pushes nothing: a data value restriction holds a data property and a literal.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}

/// <summary>A data cardinality restriction (<c>DataMinCardinality</c> / <c>DataMaxCardinality</c> / <c>DataExactCardinality</c>), qualified when <see cref="Range"/> is present.</summary>
/// <param name="Kind">The cardinality flavour.</param>
/// <param name="Cardinality">The non-negative bound.</param>
/// <param name="Property">The restricted data property.</param>
/// <param name="Range">The qualifying range, or <c>null</c> for an unqualified restriction.</param>
[DebuggerDisplay("Data{Kind}Cardinality {Cardinality}")]
public sealed record OwlDataCardinality(OwlCardinalityKind Kind, int Cardinality, NamedNode Property, OwlDataRange? Range): OwlClassExpression
{
    /// <summary>Appends nothing and pushes nothing: a data cardinality restriction holds a data property and a data range.</summary>
    public override void AppendMentionedIndividuals(List<RdfTerm> individualsToAppendTo, Stack<OwlClassExpression> expressionsToAppendTo)
    {
    }
}
