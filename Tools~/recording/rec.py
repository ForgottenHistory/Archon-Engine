"""Read Archon SessionRecorder recordings (Core.Recording): one folder per session holding meta.json and
tab-separated tables. Time tables start with tick (game hours) and date.

Where recordings are looked for: the ARCHON_RECORDINGS environment variable, else ./Recordings. A recording
argument may be a folder path, a folder name (or part of one) under that root, or omitted for the newest.
"""
import json
import os
import sys


def root():
    return os.environ.get("ARCHON_RECORDINGS") or os.path.join(os.getcwd(), "Recordings")


def _dirs(r):
    if not os.path.isdir(r):
        return []
    return sorted(d for d in os.listdir(r) if os.path.isdir(os.path.join(r, d)))


def try_find(arg=None):
    """The recording folder for ARG, or None."""
    if arg and os.path.isfile(os.path.join(arg, "meta.json")):
        return arg
    r = root()
    dirs = _dirs(r)
    if not arg:
        return os.path.join(r, dirs[-1]) if dirs else None
    if arg in dirs:
        return os.path.join(r, arg)
    match = [d for d in dirs if arg in d]
    return os.path.join(r, match[-1]) if match else None


def find(arg=None):
    """The recording folder for ARG; exits with a message when there is none."""
    found = try_find(arg)
    if found:
        return found
    r = root()
    if not os.path.isdir(r):
        sys.exit(f"no recordings: {r} does not exist (set ARCHON_RECORDINGS or run from the project root)")
    have = _dirs(r)
    sys.exit(f"no recording matches {arg!r} in {r}; have: {', '.join(have[-10:]) or 'none'}")


def tables(rec):
    return sorted(f[:-4] for f in os.listdir(rec) if f.endswith(".tsv"))


def load(rec, name):
    """(header, rows as dicts) of REC/NAME.tsv; a missing table = no rows."""
    path = os.path.join(rec, name if name.endswith(".tsv") else name + ".tsv")
    if not os.path.exists(path):
        return [], []
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().split("\n")
    header = lines[0].split("\t")
    rows = []
    for ln in lines[1:]:
        if not ln:
            continue
        p = ln.split("\t")
        p += [""] * (len(header) - len(p))
        rows.append(dict(zip(header, p)))
    return header, rows


def meta(rec):
    path = os.path.join(rec, "meta.json")
    return json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}


def ids(text):
    """A comma-separated id list as ints."""
    return [int(x) for x in text.split(",") if x] if text else []


def setup_stdout():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
