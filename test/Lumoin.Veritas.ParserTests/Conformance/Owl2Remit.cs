namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Decides whether one <see cref="Owl2TestCase"/> falls within a corpus arm's
/// remit — the metadata-only applicability question each OWL 2 suite answers
/// before it does any reasoning.
/// </summary>
/// <param name="testCase">The manifest-declared test case.</param>
/// <returns><c>true</c> when the case is the arm's to decide.</returns>
internal delegate bool Owl2RemitPredicate(Owl2TestCase testCase);

/// <summary>
/// The corpus arm a <see cref="Owl2ManifestDataAttribute"/> binds its rows to.
/// The arm's remit is a property of the manifest metadata alone
/// (<see cref="Owl2TestCase.Kinds"/>, <see cref="Owl2TestCase.Profiles"/>,
/// <see cref="Owl2TestCase.Species"/>, <see cref="Owl2TestCase.Semantics"/>),
/// so a case outside the arm's remit is filtered at the data source and never
/// materialises as a test row — it is another arm's job or out of the W3C
/// remit, not a gap this arm could ever decide.
/// </summary>
internal enum Owl2TestRemit
{
    /// <summary>Every case in the manifest (the profile-identification arm reads every test).</summary>
    All,

    /// <summary>An entailment or consistency test stated for the Direct Semantics over DL-species documents and not already owned by the RL rules runner.</summary>
    DirectSemanticsDl,

    /// <summary>An RL-marked test, for which the OWL 2 RL/RDF rules are the prescribed complete calculus.</summary>
    RlMarked,

    /// <summary>An entailment or consistency test outside the RL rules runner's remit and the Direct-Semantics DL tableau's remit — the RDF-Based-semantics residue arm's to exercise.</summary>
    RdfBasedBeyondRl,

    /// <summary>A profile-identification test, the only kind whose species markers are curated claims.</summary>
    ProfileIdentification,
}

/// <summary>
/// The remit predicates the OWL 2 corpus arms filter their rows by, one per
/// <see cref="Owl2TestRemit"/>. Each is the exact metadata-only applicability
/// condition the arm would otherwise have asserted inline — moved to the data
/// source so out-of-remit cases never materialise.
/// </summary>
internal static class Owl2Remit
{
    /// <summary>Resolves a remit to its predicate.</summary>
    /// <param name="remit">The arm's remit.</param>
    /// <returns>The predicate deciding membership.</returns>
    public static Owl2RemitPredicate For(Owl2TestRemit remit)
    {
        return remit switch
        {
            Owl2TestRemit.DirectSemanticsDl => IsDirectSemanticsDl,
            Owl2TestRemit.RlMarked => IsRlMarked,
            Owl2TestRemit.RdfBasedBeyondRl => IsRdfBasedBeyondRl,
            Owl2TestRemit.ProfileIdentification => IsProfileIdentification,
            _ => IsAny,
        };
    }

    /// <summary>Every case is in remit.</summary>
    /// <param name="testCase">The test case.</param>
    /// <returns>Always <c>true</c>.</returns>
    private static bool IsAny(Owl2TestCase testCase)
    {
        return true;
    }

    /// <summary>Whether the case carries at least one of the four entailment or consistency kinds — the kinds a reasoning arm decides, as opposed to a pure profile-identification claim.</summary>
    /// <param name="testCase">The test case.</param>
    /// <returns><c>true</c> when the case is a positive-entailment, negative-entailment, inconsistency, or consistency test.</returns>
    private static bool IsEntailmentOrConsistency(Owl2TestCase testCase)
    {
        return testCase.Kinds.Contains("PositiveEntailmentTest")
            || testCase.Kinds.Contains("NegativeEntailmentTest")
            || testCase.Kinds.Contains("InconsistencyTest")
            || testCase.Kinds.Contains("ConsistencyTest");
    }

    /// <summary>
    /// Whether the case is the Direct-Semantics DL tableau's to decide: an
    /// entailment or consistency test (the only kinds the tableau answers),
    /// not RL-marked (the RL rules runner owns those), stated for the Direct
    /// Semantics, and curated as DL species (a description-logic tableau makes
    /// no claim outside DL).
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <returns><c>true</c> when the Direct tableau owns the case.</returns>
    private static bool IsDirectSemanticsDl(Owl2TestCase testCase)
    {
        return IsEntailmentOrConsistency(testCase)
            && !testCase.Profiles.Contains("RL")
            && testCase.Semantics.Contains("DIRECT")
            && testCase.Species.Contains("DL");
    }

    /// <summary>Whether the case is RL-marked, the RL/RDF rules runner's to decide.</summary>
    /// <param name="testCase">The test case.</param>
    /// <returns><c>true</c> when the case carries the RL profile marker.</returns>
    private static bool IsRlMarked(Owl2TestCase testCase)
    {
        return testCase.Profiles.Contains("RL");
    }

    /// <summary>
    /// Whether the case is the RDF-Based-semantics residue arm's to decide: an
    /// entailment or consistency test declared for the RDF-Based semantics,
    /// outside the RL rules runner's remit (<see cref="IsRlMarked"/>) and the
    /// Direct-Semantics DL tableau's remit (<see cref="IsDirectSemanticsDl"/>).
    /// Together with those two predicates it partitions the corpus's entailment
    /// and consistency cases: every such case is claimed by exactly one of the
    /// three.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <returns><c>true</c> when the RDF-Based residue arm owns the case.</returns>
    private static bool IsRdfBasedBeyondRl(Owl2TestCase testCase)
    {
        return IsEntailmentOrConsistency(testCase)
            && testCase.Semantics.Contains("RDF-BASED")
            && !IsRlMarked(testCase)
            && !IsDirectSemanticsDl(testCase);
    }

    /// <summary>Whether the case is a profile-identification test, the only kind whose species markers are curated claims.</summary>
    /// <param name="testCase">The test case.</param>
    /// <returns><c>true</c> when the case is a profile-identification test.</returns>
    private static bool IsProfileIdentification(Owl2TestCase testCase)
    {
        return testCase.Kinds.Contains("ProfileIdentificationTest");
    }
}
