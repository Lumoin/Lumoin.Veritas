using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures the packed <see cref="GrowableBitVector"/> against the parallel
/// <see cref="List{T}"/>-of-<see cref="bool"/> idiom on the access patterns a
/// per-context boolean plane runs: a streaming scattered read, a clear paired
/// with its reinstatement, a lazily extended set over a padded gap, a
/// position-keyed record read through a past-end guard, a position-keyed fill,
/// a burst walk over a population of records, and an append series.
/// </summary>
/// <remarks>
/// <para>
/// The prebuilt records and every index sequence are built once in
/// <see cref="GlobalSetup"/> from a deterministic mixer, so both shapes see the
/// identical access pattern and the read workloads measure the read alone. The
/// three workloads whose subject is growth build a fresh record inside the
/// measured method, so their allocation lands in the reported column. The
/// clear workload restores every bit it clears, so the prebuilt record it
/// shares across invocations is in the same state at every entry.
/// </para>
/// <para>
/// No public member names <see cref="GrowableBitVector"/>: the type is internal
/// to the collections assembly while every benchmark method is public, so a
/// parameter or a return of that type would be an inconsistent-accessibility
/// error. The vectors live in private members and method locals, and every
/// method returns an <see cref="int"/> checksum so no measured work is
/// eliminated.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class GrowableBitVectorBenchmarks
{
    /// <summary>The smaller of the two measured clause-id plane populations.</summary>
    private const int SmallIdCount = 26_047;

    /// <summary>The larger of the two measured clause-id plane populations.</summary>
    private const int LargeIdCount = 31_645;

    /// <summary>The ids cleared over the smaller plane: the eliminated share of a saturation run's derived clauses, applied to that population.</summary>
    private const int SmallClearCount = 75;

    /// <summary>The ids cleared over the larger plane, on the same derivation.</summary>
    private const int LargeClearCount = 483;

    /// <summary>The positions the position-keyed record holds.</summary>
    private const int BroadcastPositionCount = 21_411;

    /// <summary>The domain the position reads are drawn from, wider than the record so a third of them land past its end.</summary>
    private const int BroadcastReadDomain = 32_768;

    /// <summary>The number of bits every read workload reads.</summary>
    private const int ReadCount = 65_536;

    /// <summary>The number of bursts the population walk runs.</summary>
    private const int BurstCount = 16_384;

    /// <summary>The reads per burst: the measured mean posting-probe length, rounded to a whole entry.</summary>
    private const int BurstLength = 4;

    /// <summary>The records the population walk selects among: the smallest power of two whose list-shaped population exceeds two mebibytes.</summary>
    private const int RecordCount = 64;

    /// <summary>The ascending sets the sparse-provenance workload runs after its first high set.</summary>
    private const int ProvenanceSetCount = 4_096;

    /// <summary>The mixer's seed for the parameter-sized sequences.</summary>
    private const ulong MixerSeed = 0x243F6A8885A308D3UL;

    /// <summary>The mixer's seed for the position-keyed read sequence, which is drawn on its own so it is identical under every parameter value and the two position-keyed rows measure identical work.</summary>
    private const ulong BroadcastMixerSeed = 0x13198A2E03707344UL;

    /// <summary>The scattered indexes the streaming read walks.</summary>
    private int[] ReadIds { get; set; } = null!;

    /// <summary>The scattered indexes the clear-and-reinstate workload toggles.</summary>
    private int[] ClearIds { get; set; } = null!;

    /// <summary>The record each burst reads from.</summary>
    private int[] BurstRecordIds { get; set; } = null!;

    /// <summary>The indexes the bursts read, four per burst.</summary>
    private int[] BurstIds { get; set; } = null!;

    /// <summary>The positions the position-keyed workload reads, a third of them past the record's end; identical under every parameter value.</summary>
    private int[] BroadcastReadIds { get; set; } = null!;

    /// <summary>The list-shaped record the streaming read walks, carrying the measured live ratio.</summary>
    private List<bool> ReadList { get; set; } = null!;

    /// <summary>The list-shaped record the clear-and-reinstate workload toggles, every bit set.</summary>
    private List<bool> ToggleList { get; set; } = null!;

    /// <summary>The list-shaped record population the burst walk selects among.</summary>
    private List<bool>[] PopulationLists { get; set; } = null!;

    /// <summary>The packed counterpart of <see cref="ReadList"/>. A mutable record is held in a field rather than a property, whose getter would hand out a copy and lose every write.</summary>
    private GrowableBitVector readVector;

    /// <summary>The packed counterpart of <see cref="ToggleList"/>, held in a field for the same reason <see cref="readVector"/> is.</summary>
    private GrowableBitVector toggleVector;

    /// <summary>The packed counterpart of <see cref="PopulationLists"/>.</summary>
    private GrowableBitVector[] PopulationVectors { get; set; } = null!;

    /// <summary>The prebuilt list-shaped position-keyed record, every position held; the position-keyed read walks it so its two parameter rows measure identical work.</summary>
    private List<bool> BroadcastList { get; set; } = null!;

    /// <summary>The packed counterpart of <see cref="BroadcastList"/>, held in a field for the same reason <see cref="readVector"/> is.</summary>
    private GrowableBitVector broadcastVector;

    /// <summary>The ids cleared at the current population.</summary>
    private int ClearCount { get; set; }

    /// <summary>The first index the sparse-provenance workload sets.</summary>
    private int ProvenanceFirstIndex { get; set; }

    /// <summary>The plane's bit count, one measured context population per value.</summary>
    [Params(SmallIdCount, LargeIdCount)]
    public int IdCount { get; set; }

    /// <summary>Builds the index sequences and the three prebuilt records, once per parameter value. The position-keyed read sequence is drawn from its own seed, so the position-keyed workload runs identical work under both parameter values and its two rows are a harness self-check.</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        ClearCount = IdCount == SmallIdCount ? SmallClearCount : LargeClearCount;
        ProvenanceFirstIndex = IdCount * 9 / 10;

        ulong state = MixerSeed;
        ReadIds = new int[ReadCount];
        for(int i = 0; i < ReadIds.Length; i++)
        {
            state = Mix(state);
            ReadIds[i] = (int)(state % (ulong)IdCount);
        }

        ClearIds = new int[ClearCount];
        for(int i = 0; i < ClearIds.Length; i++)
        {
            state = Mix(state);
            ClearIds[i] = (int)(state % (ulong)IdCount);
        }

        BurstRecordIds = new int[BurstCount];
        for(int i = 0; i < BurstRecordIds.Length; i++)
        {
            state = Mix(state);
            BurstRecordIds[i] = (int)(state % (ulong)RecordCount);
        }

        BurstIds = new int[BurstCount * BurstLength];
        for(int i = 0; i < BurstIds.Length; i++)
        {
            state = Mix(state);
            BurstIds[i] = (int)(state % (ulong)IdCount);
        }

        ulong broadcastState = BroadcastMixerSeed;
        BroadcastReadIds = new int[ReadCount];
        for(int i = 0; i < BroadcastReadIds.Length; i++)
        {
            broadcastState = Mix(broadcastState);
            BroadcastReadIds[i] = (int)(broadcastState % (ulong)BroadcastReadDomain);
        }

        ReadList = [];
        ToggleList = [];
        readVector = default;
        toggleVector = default;
        for(int i = 0; i < IdCount; i++)
        {
            ReadList.Add(true);
            ToggleList.Add(true);
            readVector.Append(true);
            toggleVector.Append(true);
        }

        for(int i = 0; i < ClearIds.Length; i++)
        {
            ReadList[ClearIds[i]] = false;
            readVector.Clear(ClearIds[i]);
        }

        PopulationLists = new List<bool>[RecordCount];
        PopulationVectors = new GrowableBitVector[RecordCount];
        for(int record = 0; record < RecordCount; record++)
        {
            List<bool> bits = [];
            for(int i = 0; i < IdCount; i++)
            {
                bits.Add(true);
                PopulationVectors[record].Append(true);
            }

            PopulationLists[record] = bits;
        }

        BroadcastList = [];
        broadcastVector = default;
        for(int position = 0; position < BroadcastPositionCount; position++)
        {
            while(BroadcastList.Count <= position)
            {
                BroadcastList.Add(false);
            }

            BroadcastList[position] = true;
            broadcastVector.Set(position);
        }
    }

    /// <summary>The streaming scattered read over the prebuilt list-shaped record.</summary>
    /// <returns>The number of set bits read.</returns>
    [Benchmark]
    public int ScatteredReadList()
    {
        int checksum = 0;
        List<bool> record = ReadList;
        for(int i = 0; i < ReadIds.Length; i++)
        {
            if(record[ReadIds[i]])
            {
                checksum++;
            }
        }

        return checksum;
    }

    /// <summary>The streaming scattered read over the prebuilt packed record.</summary>
    /// <returns>The number of set bits read.</returns>
    [Benchmark]
    public int ScatteredReadPacked()
    {
        int checksum = 0;
        for(int i = 0; i < ReadIds.Length; i++)
        {
            if(readVector[ReadIds[i]])
            {
                checksum++;
            }
        }

        return checksum;
    }

    /// <summary>The clear pass and its reinstatement over the prebuilt list-shaped record.</summary>
    /// <returns>The record's bit count.</returns>
    [Benchmark]
    public int ToggleClearsList()
    {
        List<bool> record = ToggleList;
        for(int i = 0; i < ClearIds.Length; i++)
        {
            record[ClearIds[i]] = false;
        }

        for(int i = 0; i < ClearIds.Length; i++)
        {
            record[ClearIds[i]] = true;
        }

        return record.Count;
    }

    /// <summary>The clear pass and its reinstatement over the prebuilt packed record.</summary>
    /// <returns>The record's bit count.</returns>
    [Benchmark]
    public int ToggleClearsPacked()
    {
        for(int i = 0; i < ClearIds.Length; i++)
        {
            toggleVector.Clear(ClearIds[i]);
        }

        for(int i = 0; i < ClearIds.Length; i++)
        {
            toggleVector.Set(ClearIds[i]);
        }

        return toggleVector.Count;
    }

    /// <summary>The first high set over a padded gap and the ascending sets after it, list-shaped.</summary>
    /// <returns>The sum of the set indexes.</returns>
    [Benchmark]
    public int SparseProvenanceSetsList()
    {
        int checksum = 0;
        List<bool> record = [];
        for(int i = 0; i <= ProvenanceSetCount; i++)
        {
            int id = ProvenanceFirstIndex + i;
            while(record.Count <= id)
            {
                record.Add(false);
            }

            record[id] = true;
            checksum += id;
        }

        return checksum;
    }

    /// <summary>The first high set and the ascending sets after it, packed.</summary>
    /// <returns>The sum of the set indexes.</returns>
    [Benchmark]
    public int SparseProvenanceSetsPacked()
    {
        int checksum = 0;
        GrowableBitVector record = default;
        for(int i = 0; i <= ProvenanceSetCount; i++)
        {
            int id = ProvenanceFirstIndex + i;
            record.Set(id);
            checksum += id;
        }

        return checksum;
    }

    /// <summary>The guarded reads over the prebuilt list-shaped position-keyed record, a third of them past its end; identical work under both parameter values, so the two rows are the harness self-check.</summary>
    /// <returns>The number of held positions read.</returns>
    [Benchmark]
    public int BroadcastPositionsList()
    {
        List<bool> record = BroadcastList;
        int checksum = 0;
        for(int i = 0; i < BroadcastReadIds.Length; i++)
        {
            int position = BroadcastReadIds[i];
            if(position < record.Count && record[position])
            {
                checksum++;
            }
        }

        return checksum;
    }

    /// <summary>The guarded reads over the prebuilt packed position-keyed record.</summary>
    /// <returns>The number of held positions read.</returns>
    [Benchmark]
    public int BroadcastPositionsPacked()
    {
        int checksum = 0;
        for(int i = 0; i < BroadcastReadIds.Length; i++)
        {
            if(broadcastVector.GetOrDefault(BroadcastReadIds[i]))
            {
                checksum++;
            }
        }

        return checksum;
    }

    /// <summary>The position-keyed fill, list-shaped: ascending sets into a fresh record through the padding loop.</summary>
    /// <returns>The record's length after the fill.</returns>
    [Benchmark]
    public int BroadcastFillList()
    {
        List<bool> record = [];
        for(int position = 0; position < BroadcastPositionCount; position++)
        {
            while(record.Count <= position)
            {
                record.Add(false);
            }

            record[position] = true;
        }

        return record.Count;
    }

    /// <summary>The position-keyed fill, packed: ascending sets into a fresh record.</summary>
    /// <returns>The record's length after the fill.</returns>
    [Benchmark]
    public int BroadcastFillPacked()
    {
        GrowableBitVector record = default;
        for(int position = 0; position < BroadcastPositionCount; position++)
        {
            record.Set(position);
        }

        return record.Count;
    }

    /// <summary>The burst walk over the list-shaped record population.</summary>
    /// <returns>The number of set bits read.</returns>
    [Benchmark]
    public int BurstWalkList()
    {
        int checksum = 0;
        for(int burst = 0; burst < BurstRecordIds.Length; burst++)
        {
            List<bool> record = PopulationLists[BurstRecordIds[burst]];
            int offset = burst * BurstLength;
            for(int i = 0; i < BurstLength; i++)
            {
                if(record[BurstIds[offset + i]])
                {
                    checksum++;
                }
            }
        }

        return checksum;
    }

    /// <summary>The burst walk over the packed record population.</summary>
    /// <returns>The number of set bits read.</returns>
    [Benchmark]
    public int BurstWalkPacked()
    {
        int checksum = 0;
        for(int burst = 0; burst < BurstRecordIds.Length; burst++)
        {
            int record = BurstRecordIds[burst];
            int offset = burst * BurstLength;
            for(int i = 0; i < BurstLength; i++)
            {
                if(PopulationVectors[record][BurstIds[offset + i]])
                {
                    checksum++;
                }
            }
        }

        return checksum;
    }

    /// <summary>The append series into a fresh list-shaped record.</summary>
    /// <returns>The record's bit count.</returns>
    [Benchmark]
    public int AppendGrowthList()
    {
        List<bool> record = [];
        for(int i = 0; i < IdCount; i++)
        {
            record.Add(true);
        }

        return record.Count;
    }

    /// <summary>The append series into a fresh packed record.</summary>
    /// <returns>The record's bit count.</returns>
    [Benchmark]
    public int AppendGrowthPacked()
    {
        GrowableBitVector record = default;
        for(int i = 0; i < IdCount; i++)
        {
            record.Append(true);
        }

        return record.Count;
    }

    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The state to mix.</param>
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
}
