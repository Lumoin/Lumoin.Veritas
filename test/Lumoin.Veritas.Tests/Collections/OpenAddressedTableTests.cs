using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The open-addressed table's contract, pinned against a
/// <see cref="Dictionary{TKey,TValue}"/> oracle: the same sequence of
/// exchanges leaves both reporting the same membership, the same value per
/// key, the same prior value on replacement, and the same count — including
/// the key <c>0</c>, which the control byte keeps distinct from an empty slot,
/// and enough distinct keys to force several grows.
/// </summary>
[TestClass]
internal sealed class OpenAddressedTableTests
{
    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }

    [TestMethod]
    public void ExchangeAndLookupTrackADictionaryOracleThroughCollisionsReplacementsAndGrowth()
    {
        OpenAddressedTable<int> table = new();
        Dictionary<ulong, int> oracle = [];

        //Keys drawn from a small range relative to the insert count force
        //collisions, replacements, and several grows; key 0 is in range.
        ulong state = 3;
        for(int i = 0; i < 20_000; i++)
        {
            state = Mix(state);
            ulong key = state % 2_000;

            bool existedInOracle = oracle.TryGetValue(key, out int oraclePrevious);
            bool existed = table.Exchange(key, i, out int previous);

            Assert.AreEqual(existedInOracle, existed);
            if(existedInOracle)
            {
                Assert.AreEqual(oraclePrevious, previous);
            }

            oracle[key] = i;
        }

        Assert.AreEqual(oracle.Count, table.Count);

        //Every key in range, present or absent, must agree on membership and value.
        for(ulong key = 0; key < 2_100; key++)
        {
            bool inOracle = oracle.TryGetValue(key, out int oracleValue);
            bool inTable = table.TryGetValue(key, out int tableValue);

            Assert.AreEqual(inOracle, inTable, $"membership disagreed for key {key}");
            if(inOracle)
            {
                Assert.AreEqual(oracleValue, tableValue, $"value disagreed for key {key}");
            }
        }
    }
}
