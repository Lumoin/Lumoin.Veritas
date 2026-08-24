using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Geo;

/// <summary>The outcome of one composition-closure run.</summary>
/// <param name="DerivedCount">The number of assertions appended to the caller's list.</param>
/// <param name="Consistent">Whether the closed relation graph respects joint exhaustiveness and pairwise disjointness: no ordered pair accumulated two distinct base relations, and no thing stands in a relation other than equality to itself. Derived assertions are sound only over consistent, true premises.</param>
public readonly record struct Rcc8DerivationReport(int DerivedCount, bool Consistent);

/// <summary>
/// The region-connection composition calculus: the converse map, the full 8×8 composition table, and the
/// entailment-side closure that derives new base relations from asserted ones symbolically — no geometry
/// anywhere. The closure materializes a relation between two things only where a composition-table cell is
/// a SINGLETON: a disjunctive cell is knowledge about mutually exclusive possibilities and is not
/// assertable as a triple, so the closure stays silent there rather than guess. Converses are exact and
/// always derived. Singleton-cell materialization is monotone and terminates over a finite assertion set;
/// deciding full network consistency is deliberately out of scope.
/// </summary>
public static class Rcc8Composition
{
    /// <summary>The <see cref="Rcc8Relation.Dc"/> membership bit.</summary>
    private const byte DcBit = 1 << (int)Rcc8Relation.Dc;

    /// <summary>The <see cref="Rcc8Relation.Ec"/> membership bit.</summary>
    private const byte EcBit = 1 << (int)Rcc8Relation.Ec;

    /// <summary>The <see cref="Rcc8Relation.Po"/> membership bit.</summary>
    private const byte PoBit = 1 << (int)Rcc8Relation.Po;

    /// <summary>The <see cref="Rcc8Relation.Tpp"/> membership bit.</summary>
    private const byte TppBit = 1 << (int)Rcc8Relation.Tpp;

    /// <summary>The <see cref="Rcc8Relation.Ntpp"/> membership bit.</summary>
    private const byte NtppBit = 1 << (int)Rcc8Relation.Ntpp;

    /// <summary>The <see cref="Rcc8Relation.Tppi"/> membership bit.</summary>
    private const byte TppiBit = 1 << (int)Rcc8Relation.Tppi;

    /// <summary>The <see cref="Rcc8Relation.Ntppi"/> membership bit.</summary>
    private const byte NtppiBit = 1 << (int)Rcc8Relation.Ntppi;

    /// <summary>The <see cref="Rcc8Relation.Eq"/> membership bit.</summary>
    private const byte EqBit = 1 << (int)Rcc8Relation.Eq;

    /// <summary>The full set of all eight bits — the no-information cell.</summary>
    private const byte AllBits = 0xFF;

    /// <summary>
    /// The composition table in row-major order: the cell at <c>first * 8 + second</c> holds every base
    /// relation possibly relating <c>a</c> to <c>c</c> given <c>first(a, b)</c> and <c>second(b, c)</c>.
    /// The table satisfies the identity laws on the equality row and column and the converse-symmetry law
    /// <c>comp(r1, r2) = conv(comp(conv(r2), conv(r1)))</c>, both pinned by tests, and its cells are
    /// certified against the computed topological predicates by a geometric witness sweep.
    /// </summary>
    private static byte[] Table { get; } =
    [
        //Row Dc: cells for a second operand of Dc, Ec, Po, Tpp, Ntpp, Tppi, Ntppi, Eq.
        AllBits,
        DcBit | EcBit | PoBit | TppBit | NtppBit,
        DcBit | EcBit | PoBit | TppBit | NtppBit,
        DcBit | EcBit | PoBit | TppBit | NtppBit,
        DcBit | EcBit | PoBit | TppBit | NtppBit,
        DcBit,
        DcBit,
        DcBit,
        //Row Ec.
        DcBit | EcBit | PoBit | TppiBit | NtppiBit,
        DcBit | EcBit | PoBit | TppBit | TppiBit | EqBit,
        DcBit | EcBit | PoBit | TppBit | NtppBit,
        EcBit | PoBit | TppBit | NtppBit,
        PoBit | TppBit | NtppBit,
        DcBit | EcBit,
        DcBit,
        EcBit,
        //Row Po.
        DcBit | EcBit | PoBit | TppiBit | NtppiBit,
        DcBit | EcBit | PoBit | TppiBit | NtppiBit,
        AllBits,
        PoBit | TppBit | NtppBit,
        PoBit | TppBit | NtppBit,
        DcBit | EcBit | PoBit | TppiBit | NtppiBit,
        DcBit | EcBit | PoBit | TppiBit | NtppiBit,
        PoBit,
        //Row Tpp.
        DcBit,
        DcBit | EcBit,
        DcBit | EcBit | PoBit | TppBit | NtppBit,
        TppBit | NtppBit,
        NtppBit,
        DcBit | EcBit | PoBit | TppBit | TppiBit | EqBit,
        DcBit | EcBit | PoBit | TppiBit | NtppiBit,
        TppBit,
        //Row Ntpp.
        DcBit,
        DcBit,
        DcBit | EcBit | PoBit | TppBit | NtppBit,
        NtppBit,
        NtppBit,
        DcBit | EcBit | PoBit | TppBit | NtppBit,
        AllBits,
        NtppBit,
        //Row Tppi.
        DcBit | EcBit | PoBit | TppiBit | NtppiBit,
        EcBit | PoBit | TppiBit | NtppiBit,
        PoBit | TppiBit | NtppiBit,
        PoBit | TppBit | TppiBit | EqBit,
        PoBit | TppBit | NtppBit,
        TppiBit | NtppiBit,
        NtppiBit,
        TppiBit,
        //Row Ntppi.
        DcBit | EcBit | PoBit | TppiBit | NtppiBit,
        PoBit | TppiBit | NtppiBit,
        PoBit | TppiBit | NtppiBit,
        PoBit | TppiBit | NtppiBit,
        PoBit | TppBit | NtppBit | TppiBit | NtppiBit | EqBit,
        NtppiBit,
        NtppiBit,
        NtppiBit,
        //Row Eq: composition with equality is the identity.
        DcBit,
        EcBit,
        PoBit,
        TppBit,
        NtppBit,
        TppiBit,
        NtppiBit,
        EqBit,
    ];

    /// <summary>The converse of a base relation: the proper-part members swap with their inverses, the symmetric members are self-converse.</summary>
    /// <param name="relation">The relation to convert.</param>
    /// <returns>The relation from the object back to the subject.</returns>
    public static Rcc8Relation Converse(Rcc8Relation relation)
    {
        return relation switch
        {
            Rcc8Relation.Tpp => Rcc8Relation.Tppi,
            Rcc8Relation.Tppi => Rcc8Relation.Tpp,
            Rcc8Relation.Ntpp => Rcc8Relation.Ntppi,
            Rcc8Relation.Ntppi => Rcc8Relation.Ntpp,
            _ => relation
        };
    }

    /// <summary>The elementwise converse of a relation set.</summary>
    /// <param name="relations">The set to convert.</param>
    /// <returns>The set of converses of the members.</returns>
    public static Rcc8RelationSet Converse(Rcc8RelationSet relations)
    {
        Rcc8RelationSet result = Rcc8RelationSet.Empty;
        for(int i = 0; i < 8; i++)
        {
            Rcc8Relation member = (Rcc8Relation)i;
            if(relations.Contains(member))
            {
                result = result.With(Converse(member));
            }
        }

        return result;
    }

    /// <summary>Reads a composition-table cell: every base relation possibly relating <c>a</c> to <c>c</c> given <paramref name="first"/> from <c>a</c> to <c>b</c> and <paramref name="second"/> from <c>b</c> to <c>c</c>.</summary>
    /// <param name="first">The relation from <c>a</c> to <c>b</c>.</param>
    /// <param name="second">The relation from <c>b</c> to <c>c</c>.</param>
    /// <returns>The cell's relation set.</returns>
    public static Rcc8RelationSet Compose(Rcc8Relation first, Rcc8Relation second)
    {
        return new Rcc8RelationSet(Table[((int)first << 3) | (int)second]);
    }

    /// <summary>
    /// Runs the composition closure over asserted relations and appends every newly derivable assertion:
    /// exact converses, and compositions through shared middles whose table cell is a singleton, iterated
    /// to the fixpoint. Assertions already present in the input are never re-emitted; the emission order is
    /// deterministic — pairs in first-touch order, relations in enum order.
    /// </summary>
    /// <param name="assertions">The asserted base relations.</param>
    /// <param name="derivedToAppendTo">The list the derived assertions are appended to.</param>
    /// <returns>The run's report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assertions"/> or <paramref name="derivedToAppendTo"/> is <see langword="null"/>.</exception>
    public static Rcc8DerivationReport Derive(IReadOnlyList<Rcc8Assertion> assertions, ICollection<Rcc8Assertion> derivedToAppendTo)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentNullException.ThrowIfNull(derivedToAppendTo);

        Rcc8DerivationFrame frame = new();
        foreach(Rcc8Assertion assertion in assertions)
        {
            frame.Assert(assertion);
        }

        frame.Run();

        return frame.Emit(derivedToAppendTo);
    }

    /// <summary>
    /// One closure run's state: the term-to-id map, the per-ordered-pair relation bits with the input
    /// subset marked, the per-node outgoing adjacency, and the worklist of edges whose compositions are
    /// still pending. An explicit frame in place of captured locals; the walk is a bounded worklist loop.
    /// </summary>
    private sealed class Rcc8DerivationFrame
    {
        /// <summary>The id of each seen term.</summary>
        private Dictionary<RdfTerm, int> Ids { get; } = [];

        /// <summary>The seen terms, indexed by id.</summary>
        private List<RdfTerm> Terms { get; } = [];

        /// <summary>The known relation bits per ordered pair, keyed by the packed id pair.</summary>
        private Dictionary<long, byte> Known { get; } = [];

        /// <summary>The input-asserted relation bits per ordered pair — the subset never re-emitted.</summary>
        private Dictionary<long, byte> Input { get; } = [];

        /// <summary>The ordered pairs in first-touch order — the deterministic emission order.</summary>
        private List<long> PairOrder { get; } = [];

        /// <summary>The outgoing edges per node id.</summary>
        private List<List<(int Neighbor, Rcc8Relation Relation)>> Outgoing { get; } = [];

        /// <summary>The edges whose compositions have not run yet.</summary>
        private Queue<(int Subject, Rcc8Relation Relation, int Object)> Pending { get; } = new();

        /// <summary>Seeds one input assertion: records its bits as input and adds the edge and its converse.</summary>
        /// <param name="assertion">The input assertion.</param>
        public void Assert(Rcc8Assertion assertion)
        {
            int subject = IdOf(assertion.Subject);
            int @object = IdOf(assertion.Object);
            long key = Pack(subject, @object);
            Input[key] = (byte)(Input.GetValueOrDefault(key) | (byte)(1 << (int)assertion.Relation));
            AddEdgePair(subject, assertion.Relation, @object);
        }

        /// <summary>
        /// Drains the worklist: each pending edge composes with the edges leaving its object (as the first
        /// factor) and with the reversals of the edges leaving its subject (as the second factor), and a
        /// singleton cell adds the composed edge with its converse. Later-added edges run their own
        /// compositions when they drain, so a live-count loop over the growing adjacency lists is complete.
        /// </summary>
        public void Run()
        {
            while(Pending.Count > 0)
            {
                (int subject, Rcc8Relation relation, int @object) = Pending.Dequeue();
                List<(int Neighbor, Rcc8Relation Relation)> fromObject = Outgoing[@object];
                for(int i = 0; i < fromObject.Count; i++)
                {
                    (int neighbor, Rcc8Relation second) = fromObject[i];
                    if(Compose(relation, second).TryGetSingleton(out Rcc8Relation composed))
                    {
                        AddEdgePair(subject, composed, neighbor);
                    }
                }

                List<(int Neighbor, Rcc8Relation Relation)> fromSubject = Outgoing[subject];
                for(int i = 0; i < fromSubject.Count; i++)
                {
                    (int neighbor, Rcc8Relation reversed) = fromSubject[i];
                    if(Compose(Converse(reversed), relation).TryGetSingleton(out Rcc8Relation composed))
                    {
                        AddEdgePair(neighbor, composed, @object);
                    }
                }
            }
        }

        /// <summary>Appends every derived assertion — the known bits minus the input bits, pairs in first-touch order, relations in enum order — and reports the count with the consistency verdict.</summary>
        /// <param name="derivedToAppendTo">The list the derived assertions are appended to.</param>
        /// <returns>The run's report.</returns>
        public Rcc8DerivationReport Emit(ICollection<Rcc8Assertion> derivedToAppendTo)
        {
            int count = 0;
            bool consistent = true;
            foreach(long key in PairOrder)
            {
                byte bits = Known[key];
                int subject = (int)(key >> 32);
                int @object = (int)key;
                if(System.Numerics.BitOperations.PopCount(bits) > 1 || (subject == @object && bits != (byte)(1 << (int)Rcc8Relation.Eq)))
                {
                    consistent = false;
                }

                byte derived = (byte)(bits & ~Input.GetValueOrDefault(key));
                for(int i = 0; i < 8; i++)
                {
                    if((derived & (byte)(1 << i)) != 0)
                    {
                        derivedToAppendTo.Add(new Rcc8Assertion(Terms[subject], (Rcc8Relation)i, Terms[@object]));
                        count++;
                    }
                }
            }

            return new Rcc8DerivationReport(count, consistent);
        }

        /// <summary>Reads or assigns a term's id, growing the adjacency list alongside.</summary>
        /// <param name="term">The term.</param>
        /// <returns>The term's id.</returns>
        private int IdOf(RdfTerm term)
        {
            if(Ids.TryGetValue(term, out int id))
            {
                return id;
            }

            id = Terms.Count;
            Ids[term] = id;
            Terms.Add(term);
            Outgoing.Add([]);

            return id;
        }

        /// <summary>Adds an edge and its exact converse; each direction that is new joins the adjacency, the pair bits, and the worklist.</summary>
        /// <param name="subject">The subject id.</param>
        /// <param name="relation">The relation from subject to object.</param>
        /// <param name="object">The object id.</param>
        private void AddEdgePair(int subject, Rcc8Relation relation, int @object)
        {
            AddEdge(subject, relation, @object);
            AddEdge(@object, Converse(relation), subject);
        }

        /// <summary>Adds one directed edge when it is not yet known.</summary>
        /// <param name="subject">The subject id.</param>
        /// <param name="relation">The relation from subject to object.</param>
        /// <param name="object">The object id.</param>
        private void AddEdge(int subject, Rcc8Relation relation, int @object)
        {
            long key = Pack(subject, @object);
            byte bit = (byte)(1 << (int)relation);
            byte bits = Known.GetValueOrDefault(key);
            if((bits & bit) != 0)
            {
                return;
            }

            if(bits == 0)
            {
                PairOrder.Add(key);
            }

            Known[key] = (byte)(bits | bit);
            Outgoing[subject].Add((@object, relation));
            Pending.Enqueue((subject, relation, @object));
        }

        /// <summary>Packs an ordered id pair into one dictionary key.</summary>
        /// <param name="subject">The subject id.</param>
        /// <param name="object">The object id.</param>
        /// <returns>The packed key.</returns>
        private static long Pack(int subject, int @object)
        {
            return ((long)subject << 32) | (uint)@object;
        }
    }
}
