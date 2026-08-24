using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Guards <see cref="OwlVocabulary.All"/> against drift: the enumerated term set must stay in lock-step with
/// the individual <see cref="Utf8String"/> term accessors, so a vocabulary term added later is not silently
/// missing from the corpus an editor's completion proposes.
/// </summary>
[TestClass]
internal sealed class OwlVocabularyTests
{
    /// <summary>Every public OWL term accessor (the static <see cref="Utf8String"/> properties), reflected as its IRI string.</summary>
    /// <returns>The declared OWL term IRIs.</returns>
    private static HashSet<string> DeclaredTermIris()
        => typeof(OwlVocabulary)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(Utf8String))
            .Select(property => ((Utf8String)property.GetValue(null)!).ToString())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary><see cref="OwlVocabulary.All"/> lists exactly the public term accessors — no omissions, no extras.</summary>
    [TestMethod]
    public void AllListsEveryPublicTermAccessor()
    {
        HashSet<string> declared = DeclaredTermIris();
        List<string> enumerated = OwlVocabulary.All.Select(term => term.ToString()).ToList();

        Assert.HasCount(declared.Count, enumerated, "OwlVocabulary.All and the public term accessors differ in size.");
        Assert.IsTrue(declared.SetEquals(enumerated), "OwlVocabulary.All does not match the public term accessors.");
    }

    /// <summary><see cref="OwlVocabulary.All"/> has no duplicate entries.</summary>
    [TestMethod]
    public void AllHasNoDuplicates()
    {
        List<string> iris = OwlVocabulary.All.Select(term => term.ToString()).ToList();
        HashSet<string> distinct = iris.ToHashSet(StringComparer.Ordinal);

        Assert.HasCount(distinct.Count, iris, "OwlVocabulary.All contains duplicate terms.");
    }

    /// <summary>Every enumerated term is a hash-namespace IRI in the OWL namespace with a non-empty local name.</summary>
    [TestMethod]
    public void AllTermsAreInTheOwlNamespace()
    {
        foreach(Utf8String term in OwlVocabulary.All)
        {
            string iri = term.ToString();
            Assert.IsTrue(iri.StartsWith(OwlVocabulary.Namespace, StringComparison.Ordinal), $"{iri} is not in the OWL namespace.");
            Assert.AreNotEqual(OwlVocabulary.Namespace, iri, $"{iri} has no local name.");
        }
    }
}
