namespace Lumoin.Veritas.Shacl.Components;

/// <summary>
/// Per-shape-load configuration carried through the
/// <see cref="ParameterBag"/> and made available to constraint-component
/// factories.
/// </summary>
/// <remarks>
/// <para>
/// Rather than plumbing every optional delegate into each
/// <see cref="ParameterBag"/> constructor, the loader assembles a single
/// <see cref="ShapeLoaderOptions"/> instance once per load and the bag
/// retains a single reference to it. New loader-wide configuration
/// (diagnostics sinks, strict-mode flags, custom constraint registries)
/// is added here rather than growing the bag's constructor surface.
/// </para>
/// <para>
/// All members are optional. Factories that depend on a particular
/// delegate must throw clearly when it is <c>null</c> — the loader
/// cannot know in advance which factories will run, so it cannot
/// validate upfront that required delegates were supplied.
/// </para>
/// <para>
/// <b>No <c>ShapeResolver</c>.</b> Earlier designs carried a
/// <c>ShapeResolver</c> delegate here to let shape-referencing
/// factories obtain <see cref="Shape"/> references during factory
/// invocation. The AST now holds
/// <see cref="Core.Encoding.TermId"/> values for shape references, so
/// factories no longer need to resolve them at construction time —
/// they just capture the id from the parameter bag. Resolution happens
/// at evaluation time against the loader-produced shape registry.
/// </para>
/// </remarks>
/// <param name="PatternResolver">
/// Optional resolver for <c>sh:pattern</c> values. When supplied,
/// <see cref="ParameterBag.CompilePattern"/> consults it first to obtain
/// caller-supplied (typically source-generator-compiled) matchers
/// before falling back to the per-session memo and ultimately to a
/// fresh <see cref="System.Text.RegularExpressions.Regex"/> compiled
/// with <see cref="System.Text.RegularExpressions.RegexOptions.NonBacktracking"/>
/// for ReDoS safety on untrusted input.
/// </param>
public sealed record ShapeLoaderOptions(PatternResolver? PatternResolver = null);