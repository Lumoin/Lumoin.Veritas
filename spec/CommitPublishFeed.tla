---------------------------- MODULE CommitPublishFeed ----------------------------
(***************************************************************************)
(* A TLA+ model of the commit / publish / feed protocol whose safety the   *)
(* StateLock fix protects (MutableSparqlDataset.Publish + the journal OCC). *)
(*                                                                         *)
(* Each writer commits its own value: it READs the journal head, APPENDs   *)
(* (which succeeds only if the head has not moved since the read - the      *)
(* optimistic-concurrency linearisation point - else it retries), then     *)
(* advances the FEED. The published store reflects the journal order        *)
(* absolutely (last-writer-wins here); the feed evolves by delta-fold, so   *)
(* its result depends on the order its advances run.                        *)
(*                                                                         *)
(* AtomicPublish models the fix: the feed advance runs atomically with the  *)
(* store swap, in journal order (a writer feeds only once every             *)
(* earlier-journal writer has finished). With AtomicPublish = FALSE the     *)
(* feed advance is free to reorder - the original bug.                      *)
(*                                                                         *)
(* Safety invariant FeedMatchesStore: at a terminal state the feed and the  *)
(* store agree (their last writer is the same). TLC proves it for           *)
(* AtomicPublish = TRUE and finds a counterexample for FALSE.               *)
(***************************************************************************)

(***************************************************************************)
(* Code <-> model map (keep in step with the implementation):              *)
(*                                                                         *)
(*  - Read, AppendOk, AppendConflict model                                 *)
(*    src/Lumoin.Veritas.Sparql/Execution/DatasetEditSession.cs            *)
(*    CommitAsync: it reads its base state, then JournalAppend is the OCC   *)
(*    head-CAS - the linearisation point (a base that is no longer the head *)
(*    conflicts and the request retries).                                  *)
(*                                                                         *)
(*  - Feed models the publish in                                           *)
(*    src/Lumoin.Veritas.Sparql/Execution/MutableSparqlDataset.cs Publish. *)
(*    AtomicPublish = TRUE is the fix: the store swap and the feed          *)
(*    (DefaultGraphObserver) advance run as one step under StateLock, in    *)
(*    journal order. AtomicPublish = FALSE is the pre-fix bug, where the    *)
(*    observer ran outside the lock and could reorder.                      *)
(*                                                                         *)
(*  - Reasoned mutable engine wiring: AtomicPublish now extends             *)
(*    over the SERVED-store swap and the opaque reasoning-state payload too *)
(*    - the same one step under StateLock swaps the asserted store, the     *)
(*    served store, and the payload, and advances the query rendezvous by   *)
(*    the SERVED delta while the observer keeps the ASSERTED delta. This     *)
(*    model stays ORDER-only: FeedMatchesStore checks that the feed and the *)
(*    store agree on their LAST writer (atomicity/ordering), which is       *)
(*    unchanged by the extra swap. It cannot express the two-delta CONTENT  *)
(*    split (asserted vs served) - that content-level guarantee is carried  *)
(*    by the add/retract battery and facade pins, not by TLC. The model is  *)
(*    left unrestructured; only ordering/atomicity is claimed here.          *)
(*                                                                         *)
(*  - DatasetEditSession.CommitInterleavingHook (the test seam) forces      *)
(*    this same interleaving deterministically in process.                 *)
(*                                                                         *)
(*  - spec/commit_publish_feed_check.py is the same model as a bounded      *)
(*    Python exhaustive checker (no Java/TLC needed to run it).             *)
(***************************************************************************)
EXTENDS Naturals, Sequences, FiniteSets

CONSTANTS Writers, AtomicPublish

VARIABLES
    pc,         \* pc[w]: "read" -> "append" -> "feed" -> "done"
    seen,       \* seen[w]: the head value w read at its READ step
    head,       \* the journal head version (advanced on each successful append)
    journal,    \* the sequence of writers in append (journal) order
    feedOrder   \* the sequence of writers in feed-advance order

vars == <<pc, seen, head, journal, feedOrder>>

Init ==
    /\ pc = [w \in Writers |-> "read"]
    /\ seen = [w \in Writers |-> 0]
    /\ head = 0
    /\ journal = <<>>
    /\ feedOrder = <<>>

Read(w) ==
    /\ pc[w] = "read"
    /\ seen' = [seen EXCEPT ![w] = head]
    /\ pc' = [pc EXCEPT ![w] = "append"]
    /\ UNCHANGED <<head, journal, feedOrder>>

AppendOk(w) ==
    /\ pc[w] = "append"
    /\ seen[w] = head
    /\ head' = head + 1
    /\ journal' = Append(journal, w)
    /\ pc' = [pc EXCEPT ![w] = "feed"]
    /\ UNCHANGED <<seen, feedOrder>>

AppendConflict(w) ==
    /\ pc[w] = "append"
    /\ seen[w] # head
    /\ pc' = [pc EXCEPT ![w] = "read"]
    /\ UNCHANGED <<seen, head, journal, feedOrder>>

\* The position of writer w in the journal (it has appended, so it is present).
JournalIndex(w) == CHOOSE i \in 1..Len(journal) : journal[i] = w

\* Under AtomicPublish the feed advance is gated into journal order: every
\* earlier-journal writer must already be done (its atomic publish happened).
FeedEnabled(w) ==
    \/ ~AtomicPublish
    \/ \A j \in 1..(JournalIndex(w) - 1) : pc[journal[j]] = "done"

Feed(w) ==
    /\ pc[w] = "feed"
    /\ FeedEnabled(w)
    /\ feedOrder' = Append(feedOrder, w)
    /\ pc' = [pc EXCEPT ![w] = "done"]
    /\ UNCHANGED <<seen, head, journal>>

Next == \E w \in Writers :
    \/ Read(w)
    \/ AppendOk(w)
    \/ AppendConflict(w)
    \/ Feed(w)

Spec == Init /\ [][Next]_vars

----------------------------------------------------------------------------

Terminal == \A w \in Writers : pc[w] = "done"

\* The published store's value is its last journal writer; the feed's value is
\* its last advance. At a terminal state they must agree.
FeedMatchesStore ==
    Terminal =>
        /\ Len(journal) = Cardinality(Writers)
        /\ Len(feedOrder) = Cardinality(Writers)
        /\ journal[Len(journal)] = feedOrder[Len(feedOrder)]

\* Type-correctness, a standard TLC sanity invariant.
TypeOK ==
    /\ pc \in [Writers -> {"read", "append", "feed", "done"}]
    /\ head \in Nat
    /\ seen \in [Writers -> Nat]

============================================================================
