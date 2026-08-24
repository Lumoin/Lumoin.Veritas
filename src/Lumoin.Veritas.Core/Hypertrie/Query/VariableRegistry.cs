using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Query;

/// <summary>
/// Maps query-variable names (the textual <c>?name</c> form, but
/// without the leading <c>?</c>) to compact integer identities
/// represented as <see cref="Variable"/> values.
/// </summary>
/// <remarks>
/// <para>
/// A registry is **per-query**: every <see cref="BasicGraphPattern"/>
/// has its own registry, and variable ids only have meaning within
/// that registry. Different queries do not share variables, so
/// there is no need for a global registry.
/// </para>
/// <para>
/// The registry is mutable during query construction (variables
/// are registered as they are encountered in patterns) and is
/// effectively read-only afterwards. The implementation is not
/// thread-safe; queries are constructed on a single thread.
/// </para>
/// <para>
/// Names are case-sensitive — SPARQL distinguishes <c>?x</c> from
/// <c>?X</c>, so the registry does too. Whitespace and the leading
/// <c>?</c> sigil are not part of the identity; the caller passes
/// just the name.
/// </para>
/// </remarks>
[DebuggerDisplay("VariableRegistry Count={Count}")]
public sealed class VariableRegistry
{
    private List<string> NamesByIndex { get; } = [];

    private Dictionary<string, int> IndexByName { get; } = new(StringComparer.Ordinal);

    /// <summary>The number of distinct variables registered.</summary>
    public int Count => NamesByIndex.Count;

    /// <summary>
    /// Returns the existing <see cref="Variable"/> for
    /// <paramref name="name"/>, or registers a new one if the name
    /// has not been seen.
    /// </summary>
    /// <param name="name">The variable name; must not be <c>null</c>, empty, or whitespace.</param>
    /// <returns>The variable identity for the name.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <c>null</c>, empty, or whitespace.</exception>
    public Variable GetOrAdd(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if(IndexByName.TryGetValue(name, out int existing))
        {
            return new Variable(existing);
        }

        int id = NamesByIndex.Count;
        NamesByIndex.Add(name);
        IndexByName[name] = id;

        return new Variable(id);
    }

    /// <summary>
    /// Returns the textual name for <paramref name="variable"/>.
    /// </summary>
    /// <param name="variable">The variable identity.</param>
    /// <returns>The name originally registered for the variable.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The variable's id is outside the registered range.</exception>
    public string GetName(Variable variable)
    {
        if(variable.Id < 0 || variable.Id >= NamesByIndex.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(variable),
                variable.Id,
                $"Variable id {variable.Id} is outside the registered range [0, {NamesByIndex.Count}).");
        }

        return NamesByIndex[variable.Id];
    }

    /// <summary>
    /// Returns <c>true</c> when a variable with
    /// <paramref name="name"/> has been registered;
    /// <paramref name="variable"/> receives the identity on
    /// success and the default value on failure.
    /// </summary>
    public bool TryGet(string name, out Variable variable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if(IndexByName.TryGetValue(name, out int id))
        {
            variable = new Variable(id);

            return true;
        }

        variable = default;

        return false;
    }
}
