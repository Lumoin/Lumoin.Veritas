"""
Exhaustive interleaving check of the commit/publish/feed protocol.

This is the "use Python" verification for the concurrency invariant the StateLock fix protects
(MutableSparqlDataset.Publish): the journal *append* is the linearisation point, so the journal fixes a
definite commit order; the published store reflects that order absolutely (last-writer-wins here), while the
replication feed evolves by delta-fold. The fix makes the publish (store swap + feed advance) atomic and in
journal order; the bug let the feed advance reorder relative to the journal order.

The model abstracts each commit's value to a register write (last-writer-wins) so the order-sensitivity that
non-commutative add/remove deltas have is captured cleanly. It is NOT TLA+; it is a bounded, exhaustive
safety check over every interleaving of N concurrent commits.

The companion TLA+ spec spec/CommitPublishFeed.tla is the same protocol model-checked by TLC (its
FeedMatchesStore invariant); see that file's "code <-> model map" for the implementation it mirrors:
DatasetEditSession.CommitAsync (the OCC append linearisation) and MutableSparqlDataset.Publish (the atomic,
journal-ordered store swap + feed advance under StateLock).

Run: py spec/commit_publish_feed_check.py
"""

from itertools import count


def successors(state, writer_count, atomic_publish):
    """Yields the states reachable in one step from `state`.

    A writer runs: READ (snapshot the head) -> APPEND (succeeds only if the head has not moved since READ,
    else retry) -> FEED (advance the feed). With atomic_publish the FEED step is gated into journal order
    (a writer feeds only once every earlier-journal writer is done) -- modelling the publish as one atomic,
    journal-ordered step. Without it the FEED step is free, so the feed advance can reorder.
    """
    pcs, seen, head, journal, feed_order = state
    for i in range(writer_count):
        pc = pcs[i]
        if pc == "read":
            yield (_replace(pcs, i, "append"), _replace(seen, i, head), head, journal, feed_order)
        elif pc == "append":
            if seen[i] == head:
                yield (_replace(pcs, i, "feed"), seen, head + 1, journal + (i,), feed_order)
            else:
                #The head moved since this writer read it: its append conflicts, so it retries from READ.
                yield (_replace(pcs, i, "read"), seen, head, journal, feed_order)
        elif pc == "feed":
            if atomic_publish and any(pcs[j] != "done" for j in journal[: journal.index(i)]):
                #Atomic, journal-ordered publish: an earlier-journal writer has not finished, so this one waits.
                continue
            yield (_replace(pcs, i, "done"), seen, head, journal, feed_order + (i,))


def _replace(tup, index, value):
    """Returns `tup` with element `index` set to `value`."""
    return tup[:index] + (value,) + tup[index + 1 :]


def check(writer_count, atomic_publish):
    """Explores every interleaving and returns the list of terminal states that violate feed == store."""
    start = (("read",) * writer_count, (None,) * writer_count, 0, (), ())
    seen_states = set()
    stack = [start]
    violations = []
    terminal_count = count()
    terminals = 0
    while stack:
        state = stack.pop()
        if state in seen_states:
            continue
        seen_states.add(state)

        nexts = list(successors(state, writer_count, atomic_publish))
        if not nexts:
            terminals += 1
            next(terminal_count)
            pcs, _, _, journal, feed_order = state
            #store = the absolute journal-ordered register (last journal writer wins);
            #feed = the delta-folded register (last feed-advance wins).
            store = journal[-1] if journal else None
            feed = feed_order[-1] if feed_order else None
            if store != feed:
                violations.append((journal, feed_order, store, feed))
            continue

        stack.extend(nexts)

    return violations, terminals, len(seen_states)


def main():
    """Runs the fixed and buggy models for 2 and 3 writers and prints the verdict."""
    print("commit/publish/feed exhaustive interleaving check\n")
    ok = True
    for writers in (2, 3):
        fixed_violations, fixed_terminals, fixed_states = check(writers, atomic_publish=True)
        buggy_violations, buggy_terminals, buggy_states = check(writers, atomic_publish=False)

        print(f"writers = {writers}")
        print(f"  FIXED (atomic, journal-ordered publish): {fixed_states} states, {fixed_terminals} terminals, {len(fixed_violations)} feed!=store violations")
        print(f"  BUGGY (reorderable feed advance):        {buggy_states} states, {buggy_terminals} terminals, {len(buggy_violations)} feed!=store violations")
        if buggy_violations:
            journal, feed_order, store, feed = buggy_violations[0]
            print(f"    example bug: journal order {journal} -> store={store}, but feed advanced {feed_order} -> feed={feed}")

        #The fix must hold over every interleaving; the bug must be reachable (the check is non-vacuous).
        if fixed_violations:
            ok = False
            print("  FAIL: the fixed model violated feed==store -- the model or the claim is wrong.")
        if not buggy_violations:
            ok = False
            print("  FAIL: the buggy model never violated feed==store -- the check is vacuous.")
        print()

    print("RESULT:", "PASS -- atomic publish holds for every interleaving; the bug is reachable without it." if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
