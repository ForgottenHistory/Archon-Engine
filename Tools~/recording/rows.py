"""Filter a recording's table by COLUMN NAME (so a value never matches in the wrong column).

    python rows.py [REC] TABLE --schema
    python rows.py [REC] TABLE [col=value | col!=value | col~substring | "col>N" | "col<N" ...] [--cols a,b] [--limit N] [--count COL]
    python rows.py [REC] --tables

REC: a recording folder or name (default: the newest; see rec.py for where they are looked for).
Filters are ANDed; an unknown column is an error that lists the real ones. Quote > and < filters in a shell.
Examples:
    python rows.py commands command=DeclareWarCommand
    python rows.py notes system=ai --count kind
    python rows.py control to=ABC --cols tick,date,province,from
"""
import argparse
import os
import re
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rec  # noqa: E402


def main():
    rec.setup_stdout()
    ap = argparse.ArgumentParser()
    ap.add_argument("args", nargs="*")
    ap.add_argument("--schema", action="store_true")
    ap.add_argument("--tables", action="store_true")
    ap.add_argument("--cols")
    ap.add_argument("--limit", type=int, default=200)
    ap.add_argument("--count")
    a = ap.parse_args()
    args = list(a.args)

    # The first argument is the recording when it names one and a table follows (or --tables)
    folder = None
    if args and (len(args) > 1 or a.tables) and "=" not in args[0] and rec.try_find(args[0]):
        newest = rec.try_find()
        if not (newest and os.path.exists(os.path.join(newest, args[0] + ".tsv"))) or len(args) > 1 and \
                os.path.exists(os.path.join(rec.try_find(args[0]), args[1] + ".tsv")):
            folder = args.pop(0)
    path = rec.find(folder)
    if a.tables or not args:
        print(f"{path}:")
        for t in rec.tables(path):
            h, rows = rec.load(path, t)
            print(f"  {t:16} {len(rows):8} rows  {' '.join(h)}")
        return
    name, filters = args[0], args[1:]
    header, rows = rec.load(path, name)
    if not header:
        sys.exit(f"{path} has no {name}.tsv; tables: {', '.join(rec.tables(path))}")
    if a.schema:
        print("\t".join(header))
        print(f"{len(rows)} rows in {path}")
        return

    tests = []
    for f in filters:
        m = re.match(r"^(\w+)(!=|=|~|>|<)(.*)$", f)
        if not m:
            sys.exit(f"bad filter {f!r}")
        col, op, val = m.groups()
        if col not in header:
            sys.exit(f"{name} has no column {col!r}; columns: {', '.join(header)}")
        tests.append((col, op, val))

    def ok(r):
        for col, op, val in tests:
            v = r[col]
            if op == "=" and v != val:
                return False
            if op == "!=" and v == val:
                return False
            if op == "~" and val not in v:
                return False
            if op in "<>":
                try:
                    x, y = float(v), float(val)
                except ValueError:
                    return False
                if (op == ">" and not x > y) or (op == "<" and not x < y):
                    return False
        return True

    hits = [r for r in rows if ok(r)]
    if a.count:
        if a.count not in header:
            sys.exit(f"no column {a.count!r}; columns: {', '.join(header)}")
        for k, n in Counter(r[a.count] for r in hits).most_common():
            print(f"{n:8}  {k}")
        return
    cols = a.cols.split(",") if a.cols else header
    for c in cols:
        if c not in header:
            sys.exit(f"no column {c!r}; columns: {', '.join(header)}")
    print("\t".join(cols))
    for r in hits[: a.limit]:
        print("\t".join(r[c] for c in cols))
    if len(hits) > a.limit:
        print(f"... {len(hits) - a.limit} more (--limit)")


if __name__ == "__main__":
    main()
