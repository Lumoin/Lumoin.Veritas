using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// The datatype-registry arc stage-C mirror rebase: the public verdict mirrors abstain at their zero
/// default, and the satisfiability checker rebased onto them decides mapped built-ins byte-identically to
/// before the rebase. Each row carries its certified battery id.
/// </summary>
[TestClass]
internal sealed class DatatypeVerdictMirrorTests
{
    /// <summary>MIR-MV: the membership and identity mirrors abstain at their zero default (read from default-initialized storage).</summary>
    [TestMethod]
    public void MIRMVDefaultMirrorsAreIndeterminate()
    {
        DatatypeMembership[] membership = new DatatypeMembership[1];
        DatatypeValueIdentity[] identity = new DatatypeValueIdentity[1];
        Assert.AreEqual(DatatypeMembership.Indeterminate, membership[0]);
        Assert.AreEqual(DatatypeValueIdentity.Indeterminate, identity[0]);
        Assert.AreEqual(0, (int)membership[0]);
        Assert.AreEqual(0, (int)identity[0]);
    }

    /// <summary>MIR-CNT: the count bound's zero default is Unknown, never a decisive count of zero.</summary>
    [TestMethod]
    public void MIRCNTDefaultCountBoundIsUnknown()
    {
        DatatypeCountBound[] bound = new DatatypeCountBound[1];
        Assert.AreEqual(DatatypeCountKind.Unknown, bound[0].Kind);
        Assert.AreEqual(DatatypeCountKind.Finite, DatatypeCountBound.Of(0).Kind);
        Assert.AreNotEqual(bound[0], DatatypeCountBound.Of(0));
        Assert.AreEqual(DatatypeCountKind.Infinite, DatatypeCountBound.Infinite.Kind);
    }

    /// <summary>MIR-IDENT: the rebased CompareValues decides mapped built-in numeric literals identically.</summary>
    [TestMethod]
    public void MIRIDENTCompareValuesOnMappedBuiltins()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, DatatypeSatisfiabilityChecker.CompareValues(IntLit(5), IntLit(5), DatatypeRegistry.Empty));
        Assert.AreEqual(DatatypeValueIdentity.Distinct, DatatypeSatisfiabilityChecker.CompareValues(IntLit(5), IntLit(6), DatatypeRegistry.Empty));
        Assert.AreEqual(DatatypeValueIdentity.Distinct, DatatypeSatisfiabilityChecker.CompareValues(IntLit(5), StrLit("5"), DatatypeRegistry.Empty));
        Assert.AreEqual(DatatypeValueIdentity.Indeterminate, DatatypeSatisfiabilityChecker.CompareValues(Custom("a"), Custom("b"), DatatypeRegistry.Empty));
    }

    /// <summary>MIR-IDENT: the rebased checker decides mapped-built-in satisfiability identically — disjoint families empty, a single mapped datatype non-empty.</summary>
    [TestMethod]
    public void MIRIDENTDecideConjunctionOnMappedBuiltins()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Reference(Vocabulary.Xsd.Integer), Reference(Vocabulary.Xsd.String)], DatatypeRegistry.Empty));
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Reference(Vocabulary.Xsd.Integer)], DatatypeRegistry.Empty));
    }

    /// <summary>An integer literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntLit(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(System.Globalization.CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>A string literal.</summary>
    /// <param name="value">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal StrLit(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Vocabulary.Xsd.String));
    }

    /// <summary>A literal of an unmapped custom datatype.</summary>
    /// <param name="value">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal Custom(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Utf8Strings.From("http://example.org/Custom")));
    }

    /// <summary>A named-datatype data range.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeReference Reference(Utf8String datatypeIri)
    {
        return new OwlDatatypeReference(new NamedNode(datatypeIri));
    }
}
