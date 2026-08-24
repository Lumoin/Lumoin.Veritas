using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// The <see cref="EngineNodeFamily"/> codes the OWL engines claim, declared in one place so a code is never
/// assigned twice. Codes <c>1</c>–<c>3</c> are claimed here; code <c>0</c> stays unclaimed as the unspecified
/// value of a default-constructed family.
/// </summary>
internal static class OwlEngineNodeFamilies
{
    /// <summary>The named accessors for the claimed family codes.</summary>
    extension(EngineNodeFamily)
    {
        /// <summary>The RL closure's transitivity-chain list nodes: the deterministic two-link chain structure minted per transitive property. Keys: the property's term identifier, then the zero-based list position.</summary>
        public static EngineNodeFamily TransitivityChain => EngineNodeFamily.Create(1);

        /// <summary>The RL comprehension family's bounded existential witnesses: one witness per semantic-equation instance of a some-values-from restriction. Keys: the instance, restriction, property, and filler term identifiers.</summary>
        public static EngineNodeFamily SomeValuesFromWitness => EngineNodeFamily.Create(2);

        /// <summary>The comprehension scaffold copies: fresh nodes standing for a conclusion scaffold's blank nodes in the granted structure. Key: the copy ordinal within one mint pass.</summary>
        public static EngineNodeFamily ComprehensionScaffold => EngineNodeFamily.Create(3);
    }
}
