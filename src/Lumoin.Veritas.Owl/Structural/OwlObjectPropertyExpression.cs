using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Structural;

/// <summary>
/// An OWL 2 object property expression: a named object property or the
/// inverse of one (<c>ObjectInverseOf</c>). Data and annotation properties
/// are always named, so they appear directly as <see cref="NamedNode"/>s in
/// the axiom model rather than through an expression hierarchy.
/// </summary>
public abstract record OwlObjectPropertyExpression
{
    /// <summary>The named property this expression is over — the property itself, or the property being inverted.</summary>
    public abstract NamedNode Property { get; }

    /// <summary>Whether this expression is an inverse (<c>ObjectInverseOf</c>).</summary>
    public abstract bool IsInverse { get; }
}

/// <summary>A named object property used directly.</summary>
/// <param name="Named">The property IRI node.</param>
[DebuggerDisplay("ObjectProperty {Named}")]
public sealed record OwlObjectPropertyReference(NamedNode Named): OwlObjectPropertyExpression
{
    /// <inheritdoc/>
    public override NamedNode Property
    {
        get
        {
            return Named;
        }
    }

    /// <inheritdoc/>
    public override bool IsInverse
    {
        get
        {
            return false;
        }
    }
}

/// <summary>The inverse of a named object property (<c>ObjectInverseOf</c>).</summary>
/// <param name="Inverted">The property IRI node being inverted.</param>
[DebuggerDisplay("ObjectInverseOf {Inverted}")]
public sealed record OwlInverseObjectProperty(NamedNode Inverted): OwlObjectPropertyExpression
{
    /// <inheritdoc/>
    public override NamedNode Property
    {
        get
        {
            return Inverted;
        }
    }

    /// <inheritdoc/>
    public override bool IsInverse
    {
        get
        {
            return true;
        }
    }
}
