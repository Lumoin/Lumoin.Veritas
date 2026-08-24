using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Datatypes.Automata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// The datatype-registry arc stage-C registry and acceptance rule: duplicate and built-in-IRI rejections,
/// the admissibility budget breach and white-space gate, the registration self-test rejection, and the
/// delegate escape hatch. Each row carries its certified battery id.
/// </summary>
[TestClass]
internal sealed class DatatypeRegistryTests
{
    /// <summary>REG-DUP: a second registration of the same IRI is rejected as a duplicate.</summary>
    [TestMethod]
    public void REGDUPDuplicateIriRejected()
    {
        DatatypeRegistryBuilder builder = new();
        Assert.AreEqual(RegistrationOutcomeKind.Accepted, builder.Add(new EnumeratedDatatype(Iri("Colour"), [StrLit("red")])).Kind);
        RegistrationOutcome second = builder.Add(new EnumeratedDatatype(Iri("Colour"), [StrLit("green")]));
        Assert.AreEqual(RegistrationOutcomeKind.RejectedDuplicate, second.Kind);
    }

    /// <summary>REG-BUILTIN: registering a built-in IRI is rejected — built-ins are not overridable.</summary>
    [TestMethod]
    public void REGBUILTINBuiltInIriRejected()
    {
        DatatypeRegistryBuilder builder = new();
        RegistrationOutcome outcome = builder.Add(new EnumeratedDatatype(Vocabulary.Xsd.Integer, [StrLit("1")]));
        Assert.AreEqual(RegistrationOutcomeKind.RejectedBuiltInIri, outcome.Kind);
    }

    /// <summary>DEC-ADMIT-REJ: a composite whose complement determinization breaches a small DFA ceiling is rejected with the typed budget breach.</summary>
    [TestMethod]
    public void DECADMITREJBudgetBreachRejected()
    {
        DatatypeRegistryBuilder builder = new(new AutomatonBudgets(4096, 8192, 1));
        DerivedDatatype composite = new(Iri("Choices"), new PatternDatatype(Iri("Base"), Utf8Strings.From("(a|b|c)")), []);
        RegistrationOutcome outcome = builder.Add(composite);
        Assert.AreEqual(RegistrationOutcomeKind.RejectedNotAdmissible, outcome.Kind);
        Assert.IsTrue(outcome.Breach.HasValue, "A budget-driven rejection carries the typed breach.");
        Assert.AreEqual(AutomatonBudgetKind.MaxDfaStates, outcome.Breach!.Value.Budget);
        Assert.AreEqual(1, outcome.Breach.Value.Limit);
    }

    /// <summary>DEC-WS-REJ: a lexical-identity pattern datatype over a collapse base is rejected by the white-space gate.</summary>
    [TestMethod]
    public void DECWSREJCollapseBaseRejected()
    {
        DatatypeRegistryBuilder builder = new();
        RegistrationOutcome outcome = builder.Add(new PatternDatatype(Iri("Token"), [Utf8Strings.From("a")], Vocabulary.Xsd.Token, null));
        Assert.AreEqual(RegistrationOutcomeKind.RejectedNotAdmissible, outcome.Kind);
        Assert.IsFalse(outcome.Breach.HasValue, "The white-space rejection is structural, not budget-driven.");
    }

    /// <summary>DEC-SELFTEST-REJ: a pattern datatype whose Contains contradicts its automaton is rejected by the self-test.</summary>
    [TestMethod]
    public void DECSELFTESTREJInconsistentContainsRejected()
    {
        DatatypeRegistryBuilder builder = new();
        RegistrationOutcome outcome = builder.Add(new InconsistentPatternDatatype(Iri("Liar")));
        Assert.AreEqual(RegistrationOutcomeKind.RejectedNotAdmissible, outcome.Kind);
    }

    /// <summary>The delegate escape hatch: a delegate-backed datatype is registered, is flagged self-certified, and answers through its oracle.</summary>
    [TestMethod]
    public void REGDELEGATEDelegateBackedDecidesAndIsSelfCertified()
    {
        ConstantMembershipOracle oracle = new(DatatypeMembership.In);
        DelegateBackedDatatype delegated = new(Iri("Oracle"), oracle.Answer);
        Assert.IsTrue(delegated.SelfCertified);

        DatatypeRegistryBuilder builder = new();
        Assert.AreEqual(RegistrationOutcomeKind.Accepted, builder.Add(delegated).Kind);
        DatatypeRegistry registry = builder.Build();
        Assert.IsTrue(registry.TryGet(Iri("Oracle"), out RegisteredDatatype? found));
        Assert.AreEqual(DatatypeMembership.In, found!.Contains(StrLit("anything")));
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, found.DecideConjunction(DatatypeConjunction.Empty));
    }

    /// <summary>A consistent pattern datatype over a preserve base passes admissibility and the self-test and is registered.</summary>
    [TestMethod]
    public void ConsistentPatternDatatypeAccepted()
    {
        DatatypeRegistryBuilder builder = new();
        RegistrationOutcome outcome = builder.Add(new PatternDatatype(Iri("Abc"), Utf8Strings.From("a[bc]")));
        Assert.AreEqual(RegistrationOutcomeKind.Accepted, outcome.Kind);
        Assert.IsTrue(builder.Build().TryGet(Iri("Abc"), out _));
    }

    /// <summary>The empty registry resolves nothing.</summary>
    [TestMethod]
    public void EmptyRegistryResolvesNothing()
    {
        Assert.IsFalse(DatatypeRegistry.Empty.TryGet(Iri("Anything"), out _));
    }

    /// <summary>A string literal.</summary>
    /// <param name="value">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal StrLit(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Vocabulary.Xsd.String));
    }

    /// <summary>An example-namespace datatype IRI.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String Iri(string localName)
    {
        return Utf8Strings.From("http://example.org/" + localName);
    }

    /// <summary>A pattern datatype whose membership deliberately contradicts its compiled automaton, to prove the self-test gate fires.</summary>
    private sealed class InconsistentPatternDatatype : PatternDatatype
    {
        /// <summary>Creates the inconsistent datatype over the pattern "a".</summary>
        /// <param name="datatypeIri">The datatype IRI.</param>
        public InconsistentPatternDatatype(Utf8String datatypeIri)
            : base(datatypeIri, [Utf8Strings.From("a")], Vocabulary.Xsd.String, null)
        {
        }

        /// <summary>Answers Out for every value, contradicting the automaton that accepts "a".</summary>
        /// <param name="value">The candidate value.</param>
        /// <returns>Always <see cref="DatatypeMembership.Out"/>.</returns>
        public override DatatypeMembership Contains(Literal value)
        {
            return DatatypeMembership.Out;
        }
    }

    /// <summary>A frame binding a constant membership verdict, exposing a method group as the datatype oracle without a lexical closure.</summary>
    private sealed class ConstantMembershipOracle
    {
        /// <summary>The constant membership verdict the oracle returns.</summary>
        private DatatypeMembership Verdict { get; }

        /// <summary>Creates the oracle frame.</summary>
        /// <param name="verdict">The constant membership verdict.</param>
        public ConstantMembershipOracle(DatatypeMembership verdict)
        {
            Verdict = verdict;
        }

        /// <summary>Answers the folded question from the bound frame state.</summary>
        /// <param name="question">The folded question.</param>
        /// <returns>The folded answer.</returns>
        public DatatypeAnswer Answer(in DatatypeQuestion question)
        {
            return question.Operation switch
            {
                DatatypeOperation.Contains => DatatypeAnswer.ForContains(Verdict, question.First),
                DatatypeOperation.DecideConjunction => DatatypeAnswer.ForConjunction(DatatypeSatisfiability.Satisfiable),
                _ => default
            };
        }
    }
}
