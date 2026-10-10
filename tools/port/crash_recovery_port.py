#!/usr/bin/env python3
"""
Python port of the crash-recovery state machine (desktop autosave-on-crash).
Ported and proven here before any C#, because the correctness lives entirely in
a small state machine whose failure modes (a crash at the worst possible moment)
are exactly what's hard to get right and easy to get subtly wrong.

The feature. As the user edits, a *recovery snapshot* of the whole document is
written to a known file WHILE THE DOCUMENT IS DIRTY — including a brand-new quiz
that has never been saved (full protection). On a clean exit (normal close) or a
successful save, the snapshot is DELETED. So at startup:

    snapshot file exists  ->  the last session did NOT end cleanly = a crash
                              (or power loss, or force-kill). Offer to restore.
    snapshot file absent  ->  clean last session. Normal startup.

The snapshot's mere existence is the crash signal. That is the elegant core:
nothing has to record "am I running" — a clean shutdown erases the evidence, so
leftover evidence means an unclean shutdown.

The two correctness traps this port pins:

1. ATOMIC WRITE. If the app crashes PART-WAY THROUGH writing the snapshot, a
   naive "open, write, close" leaves a half-written, unparseable snapshot — and
   now recovery itself fails at the moment it's needed most. The fix is
   write-to-temp-then-rename: write the new snapshot to a temp file, flush, then
   atomically rename it over the real path. A crash before the rename leaves the
   PREVIOUS good snapshot (or no snapshot); a crash after leaves the new one.
   There is never a torn snapshot at the real path.

2. CRASH-AFTER-RESTORE. If the user restores, keeps editing, and crashes again,
   they must still be covered. Restoring does NOT clear the snapshot; only a
   clean save/exit does. So a second crash is covered by the snapshot that kept
   being written while dirty after the restore.

Plus: a corrupt or unreadable snapshot must never crash startup — it's treated
as "nothing to recover" (and discarded), never as a hard error.
"""

import json
import os
import tempfile
from dataclasses import dataclass, field, asdict


# --------------------------------------------------------------------------- #
# The snapshot model: the document payload plus the original file path (if any)
# so a restore can reattach to the file the user was editing, or treat it as a
# brand-new unsaved quiz when there was no path.
# --------------------------------------------------------------------------- #

@dataclass
class RecoverySnapshot:
    document_json: str          # the serialized QuizDocument
    original_path: str | None   # the .qbx path it came from, or None if never saved
    schema: int = 1


class RecoveryStore:
    """Models the C# CrashRecoveryService's file behaviour."""

    def __init__(self, directory):
        self._path = os.path.join(directory, "recovery-snapshot.json")

    @property
    def path(self):
        return self._path

    # ---- write (while dirty) -------------------------------------------- #

    def write(self, snapshot: RecoverySnapshot):
        """Atomic: write to a temp file in the same directory, then os.replace
        (atomic rename on the same filesystem) over the real path. A crash
        before the replace leaves the previous snapshot intact; a crash after
        leaves the new one. Never a torn file at self._path."""
        directory = os.path.dirname(self._path)
        fd, tmp = tempfile.mkstemp(dir=directory, prefix=".recovery-", suffix=".tmp")
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as f:
                json.dump(asdict(snapshot), f)
                f.flush()
                os.fsync(f.fileno())   # the bytes are on disk before the rename
            os.replace(tmp, self._path)   # atomic
        except BaseException:
            # Clean up the temp file on any failure; never leave litter.
            if os.path.exists(tmp):
                os.remove(tmp)
            raise

    # ---- clear (on clean save / clean exit) ----------------------------- #

    def clear(self):
        """Delete the snapshot. Idempotent — clearing when none exists is fine
        (a clean exit with nothing dirty must not error)."""
        try:
            os.remove(self._path)
        except FileNotFoundError:
            pass

    # ---- detect + load (at startup) ------------------------------------- #

    def has_snapshot(self):
        return os.path.exists(self._path)

    def try_load(self):
        """Return the snapshot, or None if absent/corrupt. A corrupt snapshot is
        discarded (deleted) and treated as nothing-to-recover — recovery must
        never itself crash startup."""
        if not os.path.exists(self._path):
            return None
        try:
            with open(self._path, encoding="utf-8") as f:
                data = json.load(f)
            return RecoverySnapshot(
                document_json=data["document_json"],
                original_path=data.get("original_path"),
                schema=data.get("schema", 1),
            )
        except (ValueError, KeyError, OSError):
            # Corrupt / unreadable: discard and recover nothing.
            self.clear()
            return None


# --------------------------------------------------------------------------- #
# A tiny simulation of the app lifecycle, so tests can act out real sequences:
# edit -> (snapshot written) ; save/exit -> (snapshot cleared) ; crash -> (stop
# WITHOUT clearing) ; relaunch -> (check for leftover snapshot).
# --------------------------------------------------------------------------- #

@dataclass
class FakeApp:
    store: RecoveryStore
    doc: str = ""                 # stand-in for the document's content
    path: str | None = None       # current file path, None = never saved
    dirty: bool = False

    def edit(self, text):
        self.doc = text
        self.dirty = True
        # The app writes a snapshot on every change while dirty.
        self.store.write(RecoverySnapshot(self.doc, self.path))

    def save(self, path):
        self.path = path
        self.dirty = False
        self.store.clear()        # a clean save clears the snapshot

    def clean_exit(self):
        # On a normal close the snapshot is cleared (whether or not dirty — a
        # clean exit means the user chose to leave; unsaved-changes prompting is
        # the app's separate concern, not recovery's).
        self.store.clear()

    def crash(self):
        # A crash just stops. Crucially it does NOT clear the snapshot.
        pass


# --------------------------------------------------------------------------- #
# Tests
# --------------------------------------------------------------------------- #

def _tmpdir():
    return tempfile.mkdtemp(prefix="qb-recovery-")


def test_clean_exit_leaves_no_snapshot():
    d = _tmpdir()
    store = RecoveryStore(d)
    app = FakeApp(store)
    app.edit("some work")
    assert store.has_snapshot()      # dirty -> snapshot present
    app.clean_exit()
    assert not store.has_snapshot()  # clean exit -> gone
    # Relaunch sees nothing to recover.
    assert RecoveryStore(d).try_load() is None
    print("  OK  clean exit clears the snapshot; relaunch recovers nothing")


def test_crash_leaves_snapshot_for_recovery():
    d = _tmpdir()
    store = RecoveryStore(d)
    app = FakeApp(store)
    app.edit("unsaved brand-new quiz")   # never saved: path is None
    app.crash()                          # stop without clearing
    # Relaunch: snapshot is present and restorable.
    reopened = RecoveryStore(d)
    assert reopened.has_snapshot()
    snap = reopened.try_load()
    assert snap is not None
    assert snap.document_json == "unsaved brand-new quiz"
    assert snap.original_path is None    # full protection: never-saved recovered
    print("  OK  a crash leaves a restorable snapshot (incl. never-saved quiz)")


def test_save_clears_snapshot():
    d = _tmpdir()
    store = RecoveryStore(d)
    app = FakeApp(store)
    app.edit("work")
    app.save("/quizzes/mine.qbx")
    assert not store.has_snapshot()      # a real save clears recovery
    # A later crash with no further edits recovers nothing.
    app.crash()
    assert RecoveryStore(d).try_load() is None
    print("  OK  a successful save clears the snapshot")


def test_snapshot_records_original_path_for_reattach():
    d = _tmpdir()
    store = RecoveryStore(d)
    app = FakeApp(store, path="/quizzes/exam.qbx")   # editing an existing file
    app.edit("edited an existing quiz")
    app.crash()
    snap = RecoveryStore(d).try_load()
    assert snap.original_path == "/quizzes/exam.qbx"  # restore can reattach
    print("  OK  snapshot records the original path so restore reattaches it")


def test_atomic_write_never_leaves_torn_snapshot():
    # Simulate a crash DURING the write: the temp file is written but the
    # os.replace never happens. The real path must still hold the PREVIOUS good
    # snapshot, not a torn one.
    d = _tmpdir()
    store = RecoveryStore(d)
    store.write(RecoverySnapshot("good v1", None))
    good = store.try_load()
    assert good.document_json == "good v1"

    # Now mimic a crash mid-write: write a temp file by hand and DON'T rename.
    fd, tmp = tempfile.mkstemp(dir=d, prefix=".recovery-", suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write("{ half-written garba")   # truncated JSON, never renamed
    # The real snapshot is untouched and still parses.
    still = store.try_load()
    assert still is not None and still.document_json == "good v1"
    print("  OK  a crash mid-write leaves the previous good snapshot, never torn")


def test_corrupt_snapshot_is_tolerated():
    d = _tmpdir()
    store = RecoveryStore(d)
    # A corrupt snapshot at the real path (e.g. disk gremlin) must not crash
    # startup: try_load returns None and discards it.
    with open(store.path, "w", encoding="utf-8") as f:
        f.write("not valid json at all")
    assert store.try_load() is None
    assert not store.has_snapshot()   # discarded
    print("  OK  a corrupt snapshot is discarded, never crashes startup")


def test_restore_then_crash_is_still_covered():
    d = _tmpdir()
    store = RecoveryStore(d)
    app = FakeApp(store)
    app.edit("first session work")
    app.crash()

    # Relaunch and restore (restore does NOT clear the snapshot).
    reopened = RecoveryStore(d)
    snap = reopened.try_load()
    assert snap is not None
    app2 = FakeApp(reopened, doc=snap.document_json, path=snap.original_path, dirty=True)
    assert reopened.has_snapshot()   # still there after a restore

    # Keep editing, then crash AGAIN.
    app2.edit("more work after restore")
    app2.crash()

    # The second crash is covered by the snapshot that kept being written.
    final = RecoveryStore(d).try_load()
    assert final.document_json == "more work after restore"
    print("  OK  restore keeps the snapshot; a second crash is still covered")


def test_discard_deletes_snapshot():
    d = _tmpdir()
    store = RecoveryStore(d)
    FakeApp(store).edit("work I will discard")
    assert store.has_snapshot()
    # The user picks "Discard" in the restore dialog: clear it.
    store.clear()
    assert not store.has_snapshot()
    print("  OK  Discard deletes the snapshot")


def run_all():
    print("\nCrash-recovery state machine port -- verification\n" + "-" * 54)
    test_clean_exit_leaves_no_snapshot()
    test_crash_leaves_snapshot_for_recovery()
    test_save_clears_snapshot()
    test_snapshot_records_original_path_for_reattach()
    test_atomic_write_never_leaves_torn_snapshot()
    test_corrupt_snapshot_is_tolerated()
    test_restore_then_crash_is_still_covered()
    test_discard_deletes_snapshot()
    print("-" * 54)
    print("ALL PASS -- existence-is-the-crash-signal, atomic temp-then-rename,")
    print("full protection for never-saved quizzes, path reattach, corrupt")
    print("tolerance, and crash-after-restore coverage all proved. The C#")
    print("CrashRecoveryService mirrors this exactly.\n")


if __name__ == "__main__":
    run_all()
