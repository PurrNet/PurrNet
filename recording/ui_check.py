#!/usr/bin/env python3
"""Checks a recording's UI stream against what the scene set.

    python ui_check.py ui-test.expect.json
    python ui_check.py ui.expect.json --recording main.purrrec --parts part1.purrrec part2.purrrec

The second form checks a client's UI: its parts are merged into the server's recording (the expectations name the
player), and every chunk of every part is also read alone.

The expectations file is written by the PlayMode test RecordLocalUITests next to its recording: for a few ticks, the
elements that must be there (by name) with the values they must have, and the ones that must not be. Also checked:
every chunk starts with a snapshot and reads the same alone as after everything before it, ids never change, and the
strings that must never be recorded are nowhere in the file.
"""
import json
import sys
from pathlib import Path

import purrrec

FLAGS = {"visible": "Visible", "interactable": "Interactable", "pickable": "Pickable", "world": "WorldSpace",
         "redacted": "Redacted", "toolkit": "UIToolkit"}
EXACT = ("kind", "type", "text", "sprite", "widget", "fill", "parent", "order", "align")
NEAR = {"rotation": 0.1, "fillAmount": 1e-3, "value": 1e-3, "fontSize": 1e-3}
LISTS = {"color": 1.01, "textColor": 1.01, "worldPosition": 1e-3, "worldRotation": 1e-3, "worldSize": 1e-3}
IGNORED = ("tick", "clientTick", "viewTick", "clientTime")


def close(a, b, tolerance):
    return len(a) == len(b) and all(abs(x - y) <= tolerance for x, y in zip(a, b))


def check_element(tree, expected, errors, where):
    name = expected["name"]
    if "shows" in expected:
        shown = any(e["text"] == expected["shows"] for e in tree.elements.values())
        if shown != expected["shown"]:
            errors.append(f"{where}: text {expected['shows']!r} is {'shown' if shown else 'not shown'}")
        return None
    found = tree.find(name)
    if expected.get("absent"):
        if found:
            errors.append(f"{where}: {name} should not be recorded, found {len(found)}")
        return None
    if len(found) != 1:
        errors.append(f"{where}: expected one element named {name}, found {len(found)}")
        return None
    element = found[0]
    for key, want in expected.items():
        if key == "name":
            continue
        if key in FLAGS:
            got = FLAGS[key] in element["flags"]
        elif key == "rect":
            got = [element["x"], element["y"], element["width"], element["height"]]
            if not close(got, want, 1e-3):
                errors.append(f"{where}: {name}.rect is {got}, expected {want}")
            continue
        elif key in ("parentName", "rootName"):
            other = tree.elements.get(element["parent" if key == "parentName" else "root"])
            got = other["name"] if other else None
        elif key in LISTS:
            got = element[key]
            if not close(got, want, LISTS[key]):
                errors.append(f"{where}: {name}.{key} is {got}, expected {want}")
            continue
        elif key in NEAR:
            got = element[key]
            if abs(got - want) > NEAR[key]:
                errors.append(f"{where}: {name}.{key} is {got}, expected {want}")
            continue
        elif key in EXACT:
            got = element[key]
        else:
            errors.append(f"{where}: unknown expectation {key}")
            continue
        if got != want:
            errors.append(f"{where}: {name}.{key} is {got!r}, expected {want!r}")
    return element


def comparable(elements):
    return {i: {k: v for k, v in e.items() if k not in IGNORED} for i, e in elements.items()}


def main(path, recording=None, parts=()):
    expect = json.loads(Path(path).read_text(encoding="utf-8"))
    rec = purrrec.Recording(recording or expect["recording"])
    player = expect.get("player")
    # The files the player's UI is in, each read on its own for the chunk check: the recording itself (a host's
    # UI) and that player's parts.
    files = [rec] + [p for p in (purrrec.Recording(p) for p in parts) if (p.header or {}).get("player") == player]
    for part in files[1:]:
        rec.merge(part)
    sids = rec.custom_streams("ui", player)
    errors = []
    if not sids or len({rec.streams[s].get("player") for s in sids}) != 1:
        print(f"FAILED: expected the ui of one player, found streams {[rec.streams[s]['name'] for s in sids]}")
        return 1
    # A player who reconnected has one stream per part; in time they are one after the other.
    runs = sorted((list(purrrec.ui_rows(rec, s)) for s in sids), key=lambda r: r[0]["tick"] if r else 0)
    rows = [row for run in runs for row in run]
    run_of = [index for index, run in enumerate(runs) for _ in run]
    checked = 0

    # The tree at each phase's tick. Ids are kept for as long as a file lasts (a reconnected client's next part
    # numbers its elements anew).
    ids = {}
    ids_run = 0
    phases = sorted(expect["phases"], key=lambda p: p["tick"])
    tree = purrrec.UITree()
    at = 0
    for phase in phases:
        while at < len(rows) and rows[at]["tick"] <= phase["tick"]:
            tree.apply(rows[at])
            at += 1
        where = f"{phase['label']}@{phase['tick']}"
        if at and run_of[at - 1] != ids_run:
            ids_run = run_of[at - 1]
            ids.clear()
        if tree.screen != tuple(expect["screen"]):
            errors.append(f"{where}: screen is {tree.screen}, expected {expect['screen']}")
        for expected in phase["elements"]:
            element = check_element(tree, expected, errors, where)
            checked += len(expected) - 1
            if element is not None and ids.setdefault(expected["name"], element["id"]) != element["id"]:
                errors.append(f"{where}: {expected['name']} changed id from {ids[expected['name']]} to {element['id']}")
        for element in tree.elements.values():
            if element["parent"] and element["parent"] not in tree.elements:
                errors.append(f"{where}: #{element['id']} {element['name']} has a parent that is not in the tree")
            if element["root"] not in tree.elements:
                errors.append(f"{where}: #{element['id']} {element['name']} has a root that is not in the tree")

    # Every chunk alone gives the same tree as the whole file up to its last tick.
    chunks = 0
    for file in files:
        for sid in [s for s in file.custom_streams("ui", player) if "part" not in file.streams[s]]:
            file_rows = list(purrrec.ui_rows(file, sid))
            whole = purrrec.UITree()
            at = 0
            for index in range(len(file.chunks)):
                where = f"{file.path.name} chunk {index}"
                own = list(purrrec.ui_rows(file, sid, chunk=index))
                if not own:
                    continue
                chunks += 1
                first = own[0]
                if first["op"] != "Screen" or "Snapshot" not in first["flags"]:
                    errors.append(f"{where}: does not start with a snapshot")
                alone = purrrec.UITree()
                for row in own:
                    alone.apply(row)
                # Rows of one tick can sit on both sides of a chunk boundary: compare by position, not by tick.
                for _ in own:
                    whole.apply(file_rows[at])
                    at += 1
                if alone.screen != whole.screen or comparable(alone.elements) != comparable(whole.elements):
                    missing = set(whole.elements) ^ set(alone.elements)
                    errors.append(f"{where}: read alone it differs from the whole file at tick {own[-1]['tick']} (ids {sorted(missing)})")

    # What must never be recorded.
    raw = b"".join(payload for file in files for _, payload in file.chunks)
    for secret in expect.get("forbidden", []):
        if secret.encode("utf-8") in raw:
            errors.append(f"forbidden text {secret!r} is in the file")
        for row in rows:
            if any(isinstance(row[k], str) and secret in row[k] for k in ("text", "name", "sprite")):
                errors.append(f"forbidden text {secret!r} is in a row at tick {row['tick']}")
                break

    updates = sum(1 for r in rows if r["op"] == "Update")
    removes = sum(1 for r in rows if r["op"] == "Remove")
    print(f"{rec.path.name}{' + ' + str(len(files) - 1) + ' parts' if len(files) > 1 else ''}: {len(rows)} ui rows ({updates} updates, {removes} removals) over "
          f"{rows[-1]['tick'] - rows[0]['tick'] + 1} ticks, {chunks} chunks, {len(phases)} phases, {checked} values checked")
    for error in errors:
        print("  FAIL " + error)
    print("FAILED" if errors else "OK")
    return 1 if errors else 0


if __name__ == "__main__":
    argv, recording, parts = sys.argv[1:], None, []
    if "--parts" in argv:
        at = argv.index("--parts")
        argv, parts = argv[:at], argv[at + 1:]
    if "--recording" in argv:
        at = argv.index("--recording")
        recording = argv[at + 1]
        del argv[at:at + 2]
    if len(argv) != 1:
        print(__doc__)
        sys.exit(1)
    sys.exit(main(argv[0], recording, parts))
