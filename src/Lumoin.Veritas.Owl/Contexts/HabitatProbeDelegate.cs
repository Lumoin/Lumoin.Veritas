using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// ONE habitat probe row's match step: reads the module's told axiom surfaces
/// and answers the row's own habitat label where the row's census signal is
/// present, or <see cref="EnumerationHabitatClass.None"/> where the row
/// declines. A row whose signal admits two labels answers either its label or
/// its alternate, so the registry has exactly one row kind and one delegate
/// kind. The step is syntactic and side-effect-free.
/// </summary>
/// <param name="module">The module to probe.</param>
/// <returns>The row's label, the row's alternate label, or <see cref="EnumerationHabitatClass.None"/>.</returns>
internal delegate EnumerationHabitatClass HabitatProbeDelegate(ReasoningModule module);
