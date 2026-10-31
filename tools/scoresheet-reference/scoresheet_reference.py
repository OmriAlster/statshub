"""Prototype: parse an IBBA/Swish digital scoresheet PDF from its vector data (no OCR/vision).

Rules (confirmed by the user):
  filled dot            -> 1 point (free throw)
  diagonal slash        -> field goal (2, or 3 if the scorer's number is circled)
  circle on scorer no.  -> 3-pointer
  circle on the running-score number -> end of period (not a 3-pointer)
"""
import json, re, sys, math
import pdfplumber

HEB = re.compile(r"[\u0590-\u05FF]")


def is_black(c):
    return c is not None and all(abs(v) < 0.05 for v in c)


def is_white(c):
    return c is not None and all(v > 0.95 for v in c)


def logical(word):
    """Hebrew words come out of the PDF visually reversed; flip them."""
    t = word["text"]
    if not HEB.search(t):
        return t
    # reversing a visually-ordered RTL run also mirrors its brackets
    return t[::-1].translate(str.maketrans("()", ")("))


def read_rtl(words):
    """Join words that belong to one RTL field: rightmost word first."""
    ws = sorted(words, key=lambda w: -w["x1"])
    return " ".join(logical(w) for w in ws)


def center(o):
    return ((o["x0"] + o["x1"]) / 2, (o["top"] + o["bottom"]) / 2)


def parse(path):
    pdf = pdfplumber.open(path)
    page = pdf.pages[0]
    words = page.extract_words(extra_attrs=["non_stroking_color"])
    curves = page.curves
    warnings, errors = [], []

    # ---------------- header ----------------
    blue = lambda w: w["non_stroking_color"] == (0.0, 0.0, 1.0)
    head_names = [w for w in words if blue(w) and 38 < w["top"] < 50]
    home_name = read_rtl([w for w in head_names if w["x0"] < 300])
    away_name = read_rtl([w for w in head_names if w["x0"] >= 300])
    date = next((w["text"] for w in words if re.fullmatch(r"\d\d/\d\d/\d{4}", w["text"])), None)
    time_ = next((w["text"] for w in words if re.fullmatch(r"\d\d:\d\d", w["text"]) and w["top"] < 70), None)
    comp = read_rtl([w for w in words if blue(w) and 56 < w["top"] < 70 and w["x0"] < 270])

    # ---------------- running score grid ----------------
    grid = [w for w in words if w["text"].isdigit() and 1 <= int(w["text"]) <= 160
            and is_black(w["non_stroking_color"]) and w["x0"] > 270 and 115 < w["top"] < 600]
    by_n = {}
    for w in grid:
        by_n.setdefault(int(w["text"]), []).append(w)
    cells = {}  # (side, n) -> (cx, cy)
    for n, ws in by_n.items():
        if len(ws) != 2:
            errors.append(f"running-score number {n} found {len(ws)} times (expected 2)")
            continue
        ws.sort(key=lambda w: w["x0"])
        cells[("A", n)] = center(ws[0])
        cells[("B", n)] = center(ws[1])
    if len({n for _, n in cells}) != 160:
        errors.append(f"expected 160 grid rows, found {len({n for _, n in cells})}")

    def nearest_cell(cx, cy, max_d):
        best, bd = None, 1e9
        for k, (x, y) in cells.items():
            d = math.hypot(x - cx, y - cy)
            if d < bd:
                best, bd = k, d
        return (best, bd) if bd <= max_d else (None, bd)

    # scorer (player) numbers: coloured numeric words in the grid area
    scorer = {}
    colored = [w for w in words if w["text"].isdigit() and not is_black(w["non_stroking_color"])
               and not is_white(w["non_stroking_color"]) and w["x0"] > 255 and 115 < w["top"] < 600]
    scorer_centers = {}
    for w in colored:
        cx, cy = center(w)
        cand = [(abs(cx - x), k) for k, (x, y) in cells.items() if abs(cy - y) < 4.5]
        if not cand:
            continue
        d, k = min(cand)
        if d > 40:
            continue
        if k in scorer:
            errors.append(f"two scorer numbers on row {k}")
        scorer[k] = int(w["text"])
        scorer_centers[k] = (cx, cy)

    # marks
    in_grid = lambda c: c["x0"] > 255 and 115 < c["top"] < 600
    marks = {}  # key -> 'dot' | 'slash'
    circled_scorer, period_end = set(), {"A": [], "B": []}
    for c in curves:
        if not in_grid(c) or is_white(c.get("non_stroking_color")) and not c["stroke"]:
            continue
        w, h = c["x1"] - c["x0"], c["bottom"] - c["top"]
        cx, cy = (c["x0"] + c["x1"]) / 2, (c["top"] + c["bottom"]) / 2
        if c["fill"] and not c["stroke"] and len(c["pts"]) == 5 and 17 < w < 21 and 11 < h < 15:
            k, d = nearest_cell(cx, cy, 10)
            if k is None:
                errors.append(f"unmatched slash at {cx:.0f},{cy:.0f}")
            else:
                if k in marks:
                    errors.append(f"two marks on cell {k}")
                marks[k] = "slash"
        elif c["fill"] and not c["stroke"] and len(c["pts"]) >= 12 and 4 < w < 7 and 4 < h < 7:
            k, d = nearest_cell(cx, cy, 10)
            if k is None:
                errors.append(f"unmatched dot at {cx:.0f},{cy:.0f}")
            else:
                if k in marks:
                    errors.append(f"two marks on cell {k}")
                marks[k] = "dot"
        elif c["stroke"] and not c["fill"] and 9 < w < 11.5:
            k, d = nearest_cell(cx, cy, 4)
            if k is not None:
                period_end[k[0]].append(k[1])
                continue
            # not on a running-score number: circle around a scorer number?
            hit = [kk for kk, (x, y) in scorer_centers.items() if math.hypot(x - cx, y - cy) < 4]
            if hit:
                circled_scorer.add(hit[0])
            else:
                warnings.append(f"unclassified circle at {cx:.0f},{cy:.0f}")

    # ---------------- printed quarter totals / final ----------------
    qrow = sorted([w for w in words if 615 < w["top"] < 630 and w["x0"] > 262], key=lambda w: w["x0"])
    quarters, cur = [], None
    for w in qrow:
        if w["text"] == "A":
            cur = {"A": None, "B": None}
            quarters.append(cur)
            side = "A"
        elif w["text"] == "B":
            side = "B"
        elif w["text"].isdigit() and cur is not None:
            cur[side] = int(w["text"])
    quarters = [q for q in quarters if q["A"] is not None or q["B"] is not None]
    fin = {}
    for w in words:
        if w["text"].isdigit() and blue(w) and 660 < w["top"] < 692 and 315 < w["x0"] < 345:
            fin["A" if w["top"] < 677 else "B"] = int(w["text"])

    # ---------------- rosters ----------------
    marker_a = max((w for w in words if w["text"] == "(A)" and w["x0"] < 100), key=lambda w: w["top"])
    marker_b = max((w for w in words if w["text"] == "(B)" and w["x0"] < 100), key=lambda w: w["top"])
    code_words = [w for w in words if re.fullmatch(r"\(\d+\)", w["text"]) and w["x0"] < 60 and w["top"] > marker_a["top"]]
    rosters = {"A": [], "B": []}
    xcircles = [c for c in curves if c["stroke"] and not c["fill"] and 8 < c["x1"] - c["x0"] < 10.5 and c["x0"] < 200]
    for cw in sorted(code_words, key=lambda w: w["top"]):
        side = "A" if cw["top"] < marker_b["top"] else "B"
        band = [w for w in words if cw["top"] - 4 <= w["top"] <= cw["top"] + 9 and w["x0"] < 262]
        # skip the coach rows (they carry the code too but have no jersey number)
        num = [w for w in band if w["text"].isdigit() and 130 < w["x0"] < 142]
        if not num:
            continue
        namew = [w for w in band if HEB.search(w["text"]) or w["text"] in (",", ".")
                 or re.fullmatch(r"\(CAP\)", w["text"])]
        captain = any(w["text"] == "(CAP)" for w in namew)
        namew = [w for w in namew if w["text"] != "(CAP)" and w["x0"] > 47]
        raw = re.sub(r"[.,]", " ", read_rtl(namew)).split()
        raw = [t for t in raw if t]
        initial = raw[-1] if len(raw) > 1 and len(raw[-1]) == 1 else ""
        last = " ".join(raw[:-1] if initial else raw)
        xw = [w for w in band if w["text"] == "X" and 146 < w["x0"] < 160]
        played = bool(xw)
        starter = False
        if xw:
            xc = center(xw[0])
            starter = any(math.hypot((c["x0"] + c["x1"]) / 2 - xc[0], (c["top"] + c["bottom"]) / 2 - xc[1]) < 4
                          for c in xcircles)
        fouls = [w for w in band if w["text"] in ("P", "T", "U", "D") and w["x0"] > 160]
        rosters[side].append({
            "number": int(num[0]["text"]),
            "code": cw["text"].strip("()"),
            "lastName": last,
            "firstInitial": initial,
            "captain": captain,
            "played": played,
            "starter": starter,
            "fouls": len(fouls),
        })

    # ---------------- scoring events ----------------
    events = {"A": [], "B": []}
    for side in ("A", "B"):
        prev = 0
        keys = sorted([k for k in marks if k[0] == side], key=lambda k: k[1])
        for k in keys:
            n = k[1]
            diff = n - prev
            kind = marks[k]
            pl = scorer.get(k)
            if pl is None:
                errors.append(f"{side} score {n}: no scorer number")
            if kind == "dot":
                pts = 1
            else:
                pts = 3 if k in circled_scorer else 2
            if diff != pts:
                errors.append(f"{side} score {n}: mark implies {pts} pts but score moved {diff}")
            events[side].append({"score": n, "player": pl, "points": pts, "kind": "ft" if pts == 1 and kind == "dot" else ("fg3" if pts == 3 else "fg2")})
            prev = n

    # quarter of each event from the printed partials
    for side in ("A", "B"):
        cum, c = [], 0
        for q in quarters:
            c += q[side] or 0
            cum.append(c)
        for e in events[side]:
            e["period"] = next((i + 1 for i, v in enumerate(cum) if e["score"] <= v), len(cum) + 1)
        ends = sorted(period_end[side])
        if cum and ends[: len(cum)] != cum[: len(ends)]:
            warnings.append(f"{side}: circled period-end scores {ends} differ from printed cumulative {cum}")

    # ---------------- per-player totals + validation ----------------
    out_teams = {}
    for side, name in (("A", home_name), ("B", away_name)):
        players = []
        for p in rosters[side]:
            evs = [e for e in events[side] if e["player"] == p["number"]]
            players.append({**p,
                            "points": sum(e["points"] for e in evs),
                            "ft": sum(e["kind"] == "ft" for e in evs),
                            "fg2": sum(e["kind"] == "fg2" for e in evs),
                            "fg3": sum(e["kind"] == "fg3" for e in evs)})
        nums = {p["number"] for p in rosters[side]}
        for e in events[side]:
            if e["player"] is not None and e["player"] not in nums:
                errors.append(f"{side}: scorer #{e['player']} not on roster")
        total = sum(p["points"] for p in players)
        last_score = events[side][-1]["score"] if events[side] else 0
        if total != last_score:
            errors.append(f"{side}: player points {total} != last running score {last_score}")
        if fin.get(side) is not None and fin[side] != last_score:
            errors.append(f"{side}: printed final {fin[side]} != running score {last_score}")
        for qi, q in enumerate(quarters, 1):
            got = sum(e["points"] for e in events[side] if e["period"] == qi)
            if q[side] is not None and got != q[side]:
                errors.append(f"{side} Q{qi}: events {got} != printed {q[side]}")
        out_teams[side] = {"name": name, "points": last_score, "players": players,
                           "events": events[side]}

    return {"competition": comp, "date": date, "time": time_, "final": fin, "quarters": quarters,
            "teams": out_teams, "errors": errors, "warnings": warnings, "ok": not errors}


if __name__ == "__main__":
    r = parse(sys.argv[1])
    print(json.dumps(r, ensure_ascii=False, indent=1) if "--full" in sys.argv else json.dumps(
        {k: v for k, v in r.items() if k != "teams"}, ensure_ascii=False, indent=1))
    for s in "AB":
        t = r["teams"][s]
        print(s, t["name"], t["points"])
        for p in t["players"]:
            print("  ", p["number"], p["code"], p["lastName"], p["firstInitial"], "pts", p["points"],
                  "ft/2/3", p["ft"], p["fg2"], p["fg3"], "fouls", p["fouls"],
                  "played" if p["played"] else "-", "S" if p["starter"] else "", "C" if p["captain"] else "")
