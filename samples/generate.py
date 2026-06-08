#!/usr/bin/env python3
"""Generate the LandDoc sample corpus: synthetic land/title documents as both
Markdown (readable source) and text-based PDF (ingestible through POST /documents).

Dependency-free on purpose: the PDF writer below emits a minimal but valid
PDF 1.4 that PdfPig (the API's PdfTextExtractor) can read. Re-run any time:

    python3 samples/generate.py

Outputs:
    samples/leases/<id>.md      human-readable source
    samples/leases/<id>.pdf     ingestible PDF (text-based, no OCR needed)
    samples/manifest.json       answer key: geocoding (lat/long) + key fields per doc

Everything here is SYNTHETIC. Party names, dates, dollar amounts, and recording
data are invented. The document STRUCTURES are patterned on real instruments
(clause inventories cross-checked against public legal references -- see README),
but no real form's text is copied. Counties, parishes, and principal meridians
are real; for PLSS tracts the lat/long is COMPUTED from the township-range-section
description (see plss_centroid), so coordinates match the legal description rather
than just pointing at the county seat.
"""
from __future__ import annotations

import json
import math
import re
import textwrap
from pathlib import Path

OUT_DIR = Path(__file__).resolve().parent
LEASES_DIR = OUT_DIR / "leases"

# ---------------------------------------------------------------------------
# Minimal dependency-free PDF writer (text-based, multi-page, Helvetica).
# Only text is emitted because the ingest pipeline extracts text only (no OCR).
# ---------------------------------------------------------------------------

PAGE_W, PAGE_H = 612, 792          # US Letter, points
MARGIN = 72                        # 1 inch
FONT_SIZE = 11
LEADING = 15
WRAP_COLS = 92                     # characters per line (Helvetica 11pt fits ~85-90)


def _pdf_escape(s: str) -> str:
    return s.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def _content_stream(lines: list[str]) -> bytes:
    top = PAGE_H - MARGIN
    parts = ["BT", f"/F1 {FONT_SIZE} Tf", f"{LEADING} TL", f"{MARGIN} {top} Td"]
    for i, ln in enumerate(lines):
        if i > 0:
            parts.append("T*")
        parts.append(f"({_pdf_escape(ln)}) Tj")
    parts.append("ET")
    return "\n".join(parts).encode("latin-1")


def build_pdf(lines: list[str]) -> bytes:
    """Lay out already-wrapped text lines across paginated US-Letter pages."""
    usable = (PAGE_H - MARGIN) - MARGIN
    per_page = max(1, usable // LEADING)
    pages = [lines[i:i + per_page] for i in range(0, len(lines), per_page)] or [[]]

    n_pages = len(pages)
    page_ids, content_ids, nid = [], [], 4
    for _ in pages:
        page_ids.append(nid); nid += 1
        content_ids.append(nid); nid += 1
    total = nid - 1

    objs: list[tuple[int, bytes]] = []
    objs.append((1, b"<< /Type /Catalog /Pages 2 0 R >>"))
    kids = " ".join(f"{pid} 0 R" for pid in page_ids)
    objs.append((2, f"<< /Type /Pages /Kids [{kids}] /Count {n_pages} >>".encode()))
    objs.append((3, b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"))
    for idx, (pid, cid) in enumerate(zip(page_ids, content_ids)):
        page_body = (
            f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PAGE_W} {PAGE_H}] "
            f"/Resources << /Font << /F1 3 0 R >> >> /Contents {cid} 0 R >>"
        ).encode()
        objs.append((pid, page_body))
        stream = _content_stream(pages[idx])
        objs.append((cid, b"<< /Length %d >>\nstream\n%s\nendstream" % (len(stream), stream)))

    objs.sort()
    out = bytearray(b"%PDF-1.4\n")
    offsets: dict[int, int] = {}
    for oid, body in objs:
        offsets[oid] = len(out)
        out += f"{oid} 0 obj\n".encode() + body + b"\nendobj\n"

    xref_pos = len(out)
    out += f"xref\n0 {total + 1}\n".encode()
    out += b"0000000000 65535 f \n"
    for oid in range(1, total + 1):
        out += f"{offsets[oid]:010d} 00000 n \n".encode()
    out += f"trailer\n<< /Size {total + 1} /Root 1 0 R >>\nstartxref\n{xref_pos}\n%%EOF".encode()

    for oid in range(1, total + 1):  # self-check: xref offsets land on "<id> 0 obj"
        assert out[offsets[oid]:].startswith(f"{oid} 0 obj".encode()), f"bad offset for obj {oid}"
    return bytes(out)


# ---------------------------------------------------------------------------
# PLSS geometry: convert a Township-Range-Section-aliquot description into an
# approximate tract centroid (lat/long). PLSS is a 6-mile grid measured from
# each principal meridian's documented initial point; sections are 1-mile
# squares numbered in a boustrophedon ("snake") pattern from the NE corner.
# Accuracy is ~1-3 miles (ignores convergence, correction lines, and survey
# adjustments) -- ample for a map and for checking a tract lands in its county.
# ---------------------------------------------------------------------------

# meridian key -> (initial-point lat, initial-point lon, abbreviation in descriptions)
MERIDIANS = {
    "5th":  (34.6447,  -91.0564, "5th P.M."),
    "6th":  (40.0019,  -97.3689, "6th P.M."),
    "IM":   (34.4922,  -97.2469, "I.M."),
    "NMPM": (34.2597, -106.8867, "N.M.P.M."),
    "MDM":  (37.8817, -121.9131, "M.D.M."),
    "LA":   (31.0000,  -92.4042, "Louisiana Meridian"),
    "MPM":  (45.7869, -111.6592, "M.P.M."),
}

MI_PER_DEG_LAT = 69.0
_DIRWORD = {"N": "North", "S": "South", "E": "East", "W": "West"}


def sec_rowcol(sec: int) -> tuple[int, int]:
    """Row (0=north tier) and column (0=west) of a section in its township."""
    row = (sec - 1) // 6
    idx = (sec - 1) % 6
    col = (5 - idx) if row % 2 == 0 else idx   # even rows number E->W, odd W->E
    return row, col


def parse_aliquot(aliquot: str | None) -> tuple[float, float]:
    """Center of an aliquot within a unit section, as (x east, y north) in [0,1].
    Reads quarter/half calls right-to-left (rightmost = largest subdivision)."""
    if not aliquot:
        return 0.5, 0.5
    tokens = re.findall(r"N[EW]/4|S[EW]/4|[NSEW]/2", aliquot)
    if not tokens:
        return 0.5, 0.5
    x0, x1, y0, y1 = 0.0, 1.0, 0.0, 1.0
    for t in reversed(tokens):
        xm, ym = (x0 + x1) / 2, (y0 + y1) / 2
        if t == "NE/4": x0, y0 = xm, ym
        elif t == "NW/4": x1, y0 = xm, ym
        elif t == "SE/4": x0, y1 = xm, ym
        elif t == "SW/4": x1, y1 = xm, ym
        elif t == "N/2": y0 = ym
        elif t == "S/2": y1 = ym
        elif t == "E/2": x0 = xm
        elif t == "W/2": x1 = xm
    return (x0 + x1) / 2, (y0 + y1) / 2


def _section_centroid_miles(twp, twp_dir, rng, rng_dir, sec, aliquot):
    row, col = sec_rowcol(sec)
    ax, ay = parse_aliquot(aliquot)
    north_edge = twp * 6 if twp_dir == "N" else -((twp - 1) * 6)
    north = north_edge - (row + 0.5) + (ay - 0.5)
    west_edge = (rng - 1) * 6 if rng_dir == "E" else -(rng * 6)
    east = west_edge + (col + 0.5) + (ax - 0.5)
    return north, east


def plss_centroid(p: dict) -> tuple[float, float]:
    lat0, lon0, _ = MERIDIANS[p["mer"]]
    secs = p["sec"] if isinstance(p["sec"], list) else [p["sec"]]
    norths, easts = [], []
    for s in secs:
        n, e = _section_centroid_miles(p["twp"], p["twp_dir"], p["rng"], p["rng_dir"], s,
                                       p.get("aliquot"))
        norths.append(n); easts.append(e)
    north, east = sum(norths) / len(norths), sum(easts) / len(easts)
    lat = lat0 + north / MI_PER_DEG_LAT
    lon = lon0 + east / (69.172 * math.cos(math.radians(lat)))
    return round(lat, 4), round(lon, 4)


def plss_legal(p: dict) -> str:
    _, _, abbr = MERIDIANS[p["mer"]]
    base = (f"Township {p['twp']} {_DIRWORD[p['twp_dir']]}, "
            f"Range {p['rng']} {_DIRWORD[p['rng_dir']]}, {abbr}")
    aliquot = p.get("aliquot")
    if isinstance(p["sec"], list):
        secs = " and ".join(str(s) for s in p["sec"])
        tail = " (all)" if aliquot in (None, "all") else f": {aliquot}"
        return f"{base}, Sections {secs}{tail}"
    tail = "" if aliquot in (None, "all") else f": {aliquot}"
    return f"{base}, Section {p['sec']}{tail}"


def haversine_mi(a: tuple[float, float], b: tuple[float, float]) -> float:
    r = 3958.8
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return round(2 * r * math.asin(math.sqrt(h)), 1)


# ---------------------------------------------------------------------------
# Block model -> Markdown + wrapped plain text (for the PDF). kind in
# {"h1", "h2", "p", "blank", "table"}; a "table" block's payload is a list of
# rows (each a list of cells), the first row being the header. Tables render as
# pipe tables in Markdown and as padded columns in the PDF -- deliberately
# "messy" input for testing how fixed-window chunking handles tabular data.
# ---------------------------------------------------------------------------

def _table_md(rows: list[list[str]]) -> str:
    head = "| " + " | ".join(rows[0]) + " |"
    sep = "| " + " | ".join("---" for _ in rows[0]) + " |"
    body = ["| " + " | ".join(str(c) for c in r) + " |" for r in rows[1:]]
    return "\n".join([head, sep, *body])


def _table_lines(rows: list[list[str]]) -> list[str]:
    cols = len(rows[0])
    widths = [max(len(str(r[i])) for r in rows) for i in range(cols)]
    out: list[str] = []
    for ri, r in enumerate(rows):
        out.append("  ".join(str(r[i]).ljust(widths[i]) for i in range(cols)))
        if ri == 0:
            out.append("  ".join("-" * widths[i] for i in range(cols)))
    return out


def to_markdown(blocks: list[tuple[str, object]]) -> str:
    md: list[str] = []
    for kind, text in blocks:
        if kind == "h1":
            md.append(f"# {text}\n")
        elif kind == "h2":
            md.append(f"## {text}\n")
        elif kind == "blank":
            md.append("")
        elif kind == "table":
            md.append(_table_md(text) + "\n")
        else:
            md.append(text + "\n")
    return "\n".join(md).strip() + "\n"


def to_lines(blocks: list[tuple[str, object]]) -> list[str]:
    lines: list[str] = []
    for kind, text in blocks:
        if kind == "blank":
            lines.append("")
        elif kind in ("h1", "h2"):
            lines.append(text.upper() if kind == "h1" else text)
            lines.append("")
        elif kind == "table":
            lines.extend(_table_lines(text))
        else:
            lines.extend(textwrap.wrap(text, WRAP_COLS) or [""])
    return lines


# ---------------------------------------------------------------------------
# Reusable clause fragments
# ---------------------------------------------------------------------------

def _notary(state, county_label, county, who):
    return [
        ("blank", ""),
        ("p", f"STATE OF {state.upper()}"),
        ("p", f"{county_label.upper()} OF {county.upper()}"),
        ("blank", ""),
        ("p", f"This instrument was acknowledged before me on the date written above by {who}."),
        ("blank", ""),
        ("p", "____________________________________"),
        ("p", "Notary Public"),
        ("p", "My commission expires: ______________"),
    ]


def _recording_block(rec):
    return [
        ("p", "[RECORDING DATA]"),
        ("p", f"Instrument No.: {rec['instrument_no']}"),
        ("p", f"Book/Volume: {rec['book']}   Page: {rec['page']}"),
        ("p", f"Recorded: {rec.get('recorded', '____________')}   {rec.get('recorder', 'County Clerk')}"),
    ]


# Clause fragments cross-checked against public references (paraphrased, not copied).
MOTHER_HUBBARD = (
    "This lease also covers and includes all land owned or claimed by Lessor adjacent or "
    "contiguous to the land particularly described above, whether the same be in said survey "
    "or in adjacent surveys, although not included within the boundaries above (the "
    "\"Mother Hubbard\" clause).")
CONTINUOUS_OPS = (
    "If at the expiration of the primary term oil or gas is not being produced but Lessee is "
    "then engaged in drilling or reworking operations, this lease shall remain in force so "
    "long as such operations are prosecuted with no cessation of more than ninety (90) "
    "consecutive days. If a dry hole is drilled, the lease shall not terminate if Lessee "
    "commences additional operations within ninety (90) days thereafter.")
FORCE_MAJEURE = (
    "When operations or production are prevented or delayed by force majeure -- including acts "
    "of God, war, governmental regulation or delay in issuing permits, inability to obtain "
    "materials, or lack of available market or transportation -- this lease shall not "
    "terminate, the time of such delay shall not be counted against Lessee, and the lease "
    "shall be extended for so long as operations are so prevented or delayed.")
PUGH_CLAUSE = (
    "Pugh Clause: Upon expiration of the primary term, this lease shall terminate as to all "
    "lands and all depths not then included within a producing or pooled unit, unless Lessee "
    "is then conducting continuous operations as provided herein.")


# ---------------------------------------------------------------------------
# Templates (one per instrument style)
# ---------------------------------------------------------------------------

def t_oil_gas_lease(d):
    cl = d.get("county_label", "County")
    b = [
        ("h1", "Oil and Gas Lease"),
        ("p", f"(Producers 88 -- Paid-Up) Form No. {d['form_no']}"),
        ("blank", ""),
        ("p", f"THIS AGREEMENT made this {d['effective_date']}, by and between {d['lessor']} "
              f"(\"Lessor\", whether one or more), and {d['lessee']} (\"Lessee\")."),
        ("blank", ""),
        ("h2", "1. Granting Clause"),
        ("p", f"Lessor, in consideration of {d['bonus']} and the covenants herein, grants, leases, "
              f"and lets exclusively unto Lessee the land described below for the purpose of "
              f"exploring, drilling, operating for, and producing oil and gas, together with rights "
              f"of ingress and egress and the use of the surface as reasonably necessary."),
        ("p", MOTHER_HUBBARD),
        ("h2", "2. Description of Leased Premises"),
        ("p", f"The leased premises are situated in {d['county']} {cl}, {d['state']}, and described "
              f"as: {d['legal']}, containing {d['acres']} acres, more or less (the \"Leased "
              f"Premises\")."),
        ("h2", "3. Primary Term"),
        ("p", f"This lease shall remain in force for a primary term of {d['term']} from the "
              f"effective date and as long thereafter as oil or gas is produced in paying "
              f"quantities from the Leased Premises or lands pooled therewith (the habendum)."),
        ("h2", "4. Royalty"),
        ("p", f"Lessee shall pay Lessor a royalty of {d['royalty']} of the gross proceeds of all "
              f"oil and gas produced and sold, free of the costs of production but bearing its "
              f"proportionate share of post-production costs as permitted by law."),
        ("h2", "5. Shut-In Royalty"),
        ("p", f"If a well capable of producing gas is shut in, Lessee may maintain this lease by "
              f"paying a shut-in royalty of {d['shut_in']} per net mineral acre per year."),
        ("h2", "6. Pooling and Unitization"),
        ("p", "Lessee may pool the Leased Premises with other lands to form a unit not exceeding "
              "640 acres (plus 10% tolerance) for an oil well or 1,280 acres for a horizontal gas "
              "well, in conformity with applicable spacing and density rules."),
        ("h2", "7. Continuous Operations and Dry Hole"),
        ("p", CONTINUOUS_OPS),
        ("h2", "8. Force Majeure"),
        ("p", FORCE_MAJEURE),
        ("h2", "9. Pugh Clause"),
        ("p", PUGH_CLAUSE),
        ("h2", "10. Warranty, Surrender, and Successors"),
        ("p", "Lessor warrants and agrees to defend title to the Leased Premises and grants Lessee "
              "the right to redeem for Lessor any unpaid taxes or mortgages. Lessee may surrender "
              "this lease, in whole or in part, by recording a release. This lease binds the heirs, "
              "successors, and assigns of the parties."),
        ("blank", ""),
        ("p", "IN WITNESS WHEREOF, the parties execute this lease as of the effective date."),
        ("blank", ""),
        ("p", f"LESSOR: {d['lessor']}"),
        ("p", f"LESSEE: {d['lessee']}"),
    ]
    return b + _notary(d["state"], cl, d["county"], d["lessor"])


def t_paidup_modern(d):
    return [
        ("h1", "Paid-Up Oil and Gas Lease"),
        ("p", f"Effective Date: {d['effective_date']}"),
        ("p", f"Lessor: {d['lessor']}"),
        ("p", f"Lessee: {d['lessee']}"),
        ("p", f"County: {d['county']}, {d['state']}"),
        ("blank", ""),
        ("h2", "Recitals"),
        ("p", "Lessor owns an interest in the oil, gas, and other minerals underlying the lands "
              "described herein and desires to lease the same to Lessee for development."),
        ("h2", "Leased Lands"),
        ("p", f"{d['legal']}, containing approximately {d['acres']} net mineral acres in "
              f"{d['county']} County, {d['state']}."),
        ("p", MOTHER_HUBBARD),
        ("h2", "Consideration and Bonus"),
        ("p", f"Lessee has paid Lessor {d['bonus']} as a paid-up bonus, the receipt of which is "
              f"acknowledged, covering the full primary term with no delay rentals due."),
        ("h2", "Term"),
        ("p", f"Primary term of {d['term']}, and so long thereafter as operations or production "
              f"continue on the leased lands or lands pooled therewith."),
        ("h2", "Royalty"),
        ("p", f"{d['royalty']} of production, delivered or paid free of the costs of exploration, "
              f"drilling, and production."),
        ("h2", "Continuous Development"),
        ("p", "A continuous-development clause requires Lessee to commence a new well within 180 "
              "days of completion of the prior well to hold acreage outside a producing unit; this "
              "lease covers all depths."),
        ("h2", "Force Majeure"),
        ("p", FORCE_MAJEURE),
        ("h2", "Pugh Clause"),
        ("p", PUGH_CLAUSE),
        ("h2", "Execution"),
        ("p", f"Executed by {d['lessor']} (Lessor) and {d['lessee']} (Lessee) effective "
              f"{d['effective_date']}."),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_metes_bounds_lease(d):
    return [
        ("h1", "Oil and Gas Lease (Appalachian Form)"),
        ("p", f"This Lease, made and entered into on {d['effective_date']}, between {d['lessor']}, "
              f"of {d['township']}, {d['county']} County, {d['state']} (\"Lessor\"), and "
              f"{d['lessee']} (\"Lessee\")."),
        ("blank", ""),
        ("h2", "Witnesseth"),
        ("p", f"That for and in consideration of {d['bonus']} and the covenants hereinafter "
              f"contained, Lessor does hereby grant, demise, lease, and let unto Lessee, for the "
              f"sole and only purpose of exploring for and producing oil and gas, all that certain "
              f"tract of land described as follows:"),
        ("h2", "Description (Metes and Bounds)"),
        ("p", d['legal']),
        ("p", f"Containing {d['acres']} acres, more or less, being the same premises described in "
              f"{d['source_deed']}, situate in {d['township']}, {d['county']} County, {d['state']}."),
        ("h2", "Habendum / Term"),
        ("p", f"To have and to hold for a term of {d['term']} from the date hereof (the primary "
              f"term) and so long thereafter as oil or gas is produced in paying quantities."),
        ("h2", "Royalty"),
        ("p", f"Lessee covenants to pay Lessor a royalty of {d['royalty']} of the net proceeds at "
              f"the wellhead for all gas produced and marketed, and {d['oil_royalty']} of all oil "
              f"produced and saved from the leased premises."),
        ("h2", "Free Gas"),
        ("p", "Lessor shall have the right to use up to 200,000 cubic feet of gas per year, free of "
              "charge, for one dwelling on the premises, at Lessor's own risk."),
        ("h2", "Force Majeure"),
        ("p", FORCE_MAJEURE),
        ("blank", ""),
        ("p", f"WITNESS the hand and seal of {d['lessor']}, Lessor."),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_memorandum(d):
    blocks = [("h1", "Memorandum of Oil and Gas Lease"), ("p", "(Short Form for Recording)"),
              ("blank", "")]
    blocks += _recording_block(d["recording"])
    blocks += [
        ("blank", ""),
        ("p", "Notice is hereby given of the following Oil and Gas Lease, the full terms of which "
              "are incorporated herein by reference:"),
        ("p", f"Lessor: {d['lessor']}"),
        ("p", f"Lessee: {d['lessee']}"),
        ("p", f"Effective Date: {d['effective_date']}"),
        ("p", f"Primary Term: {d['term']}"),
        ("p", f"Royalty: {d['royalty']}"),
        ("h2", "Leased Premises"),
        ("p", f"{d['legal']}, containing {d['acres']} acres, more or less, {d['county']} County, "
              f"{d['state']}."),
        ("p", "This Memorandum is executed and recorded to give notice of the Lease and is not a "
              "complete statement of its terms. In the event of conflict, the Lease controls."),
    ]
    return blocks + _notary(d["state"], "County", d["county"], d["lessor"])


def t_mineral_deed(d):
    return [
        ("h1", "Mineral Deed"),
        ("p", f"KNOW ALL PERSONS BY THESE PRESENTS, that {d['grantor']} (\"Grantor\"), for and in "
              f"consideration of {d['consideration']}, the receipt of which is acknowledged, does "
              f"hereby GRANT, SELL, and CONVEY unto {d['grantee']} (\"Grantee\") the following:"),
        ("h2", "Mineral Interest Conveyed (Granting Clause)"),
        ("p", f"An undivided {d['interest']} interest in and to all of the oil, gas, and other "
              f"minerals in, under, and that may be produced from the following described land, "
              f"together with the right of ingress and egress and the executive right to lease:"),
        ("h2", "Land"),
        ("p", f"{d['legal']}, containing {d['acres']} acres, more or less, situated in {d['county']} "
              f"County, {d['state']}."),
        ("h2", "Habendum"),
        ("p", "TO HAVE AND TO HOLD the above-described mineral interest unto Grantee, Grantee's "
              "heirs, successors, and assigns forever."),
        ("h2", "Subject To / Existing Lease"),
        ("p", f"This conveyance is made subject to any valid and subsisting oil and gas lease of "
              f"record, but covers and includes {d['interest']} of all rentals and royalties "
              f"accruing thereunder from and after the date hereof."),
        ("h2", "Warranty"),
        ("p", f"Grantor binds Grantor and Grantor's heirs to WARRANT AND FOREVER DEFEND title to "
              f"the interest conveyed unto Grantee against every person lawfully claiming the same "
              f"({d['warranty']} warranty)."),
        ("blank", ""),
        ("p", f"EXECUTED this {d['effective_date']}."),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_royalty_deed(d):
    return [
        ("h1", "Royalty Deed"),
        ("p", f"THIS ROYALTY DEED is made {d['effective_date']} by {d['grantor']} (\"Grantor\") to "
              f"{d['grantee']} (\"Grantee\")."),
        ("h2", "Granting Clause"),
        ("p", f"For valuable consideration of {d['consideration']}, Grantor grants and conveys unto "
              f"Grantee a perpetual, non-participating royalty interest equal to {d['interest']} of "
              f"all oil, gas, and other minerals produced, saved, and sold from the land described "
              f"below, free and clear of all costs of exploration, development, and production."),
        ("h2", "Land"),
        ("p", f"{d['legal']}, containing {d['acres']} acres, more or less, {d['county']} County, "
              f"{d['state']}."),
        ("h2", "Non-Participating Nature"),
        ("p", "The interest conveyed is a non-participating royalty: Grantee shall not have the "
              "right to execute leases, to receive bonus or delay rentals, or to join in the making "
              "of oil and gas leases, all such executive rights being reserved to the mineral "
              "owner."),
        ("blank", ""),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_warranty_deed(d):
    return [
        ("h1", "General Warranty Deed"),
        ("p", f"THE STATE OF {d['state'].upper()}"),
        ("p", f"COUNTY OF {d['county'].upper()}"),
        ("blank", ""),
        ("p", f"That {d['grantor']} (\"Grantor\"), for and in consideration of the sum of "
              f"{d['consideration']} cash in hand paid by {d['grantee']} (\"Grantee\"), the receipt "
              f"and sufficiency of which are acknowledged, has GRANTED, SOLD, and CONVEYED, and by "
              f"these presents does GRANT, SELL, and CONVEY unto Grantee the following real "
              f"property:"),
        ("h2", "Property"),
        ("p", f"{d['legal']}, containing {d['acres']} acres, more or less, situated in {d['county']} "
              f"County, {d['state']}, together with all improvements thereon (the \"Property\")."),
        ("h2", "Reservations and Exceptions"),
        ("p", f"Grantor RESERVES unto Grantor, Grantor's heirs and assigns, an undivided "
              f"{d['reservation']} of all oil, gas, and other minerals in, on, and under the "
              f"Property. This conveyance is subject to all easements, restrictions, and mineral "
              f"reservations of record. (Note: where a grantor reserves a mineral fraction and "
              f"warrants title, the Duhig rule may govern competing reservations.)"),
        ("h2", "Habendum and Warranty"),
        ("p", "TO HAVE AND TO HOLD the Property unto Grantee, Grantee's heirs, successors, and "
              "assigns forever; and Grantor binds Grantor and Grantor's heirs to WARRANT AND "
              "FOREVER DEFEND all and singular the Property unto Grantee against every person "
              "lawfully claiming the same."),
        ("blank", ""),
        ("p", f"EXECUTED on {d['effective_date']}."),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_quitclaim(d):
    return [
        ("h1", "Quitclaim Deed"),
        ("p", f"THIS QUITCLAIM DEED, executed {d['effective_date']}, by {d['grantor']} (\"Grantor\") "
              f"to {d['grantee']} (\"Grantee\")."),
        ("p", f"WITNESSETH, that Grantor, for and in consideration of {d['consideration']}, does "
              f"hereby remise, release, and forever QUITCLAIM unto Grantee all of Grantor's right, "
              f"title, and interest, if any, in and to the following described property:"),
        ("h2", "Property"),
        ("p", f"{d['legal']}, containing {d['acres']} acres, more or less, situated in "
              f"{d['county']} County, {d['state']}."),
        ("p", "TO HAVE AND TO HOLD the same, together with all appurtenances, unto Grantee forever. "
              "This deed conveys only such interest as Grantor may hold and contains no warranty of "
              "title, express or implied."),
        ("blank", ""),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_surface_use(d):
    return [
        ("h1", "Surface Use and Damage Agreement"),
        ("p", f"This Surface Use and Damage Agreement (\"Agreement\") is entered into on "
              f"{d['effective_date']} between {d['lessor']} (\"Surface Owner\") and {d['lessee']} "
              f"(\"Operator\")."),
        ("h2", "Recitals"),
        ("p", f"WHEREAS Operator holds oil and gas leasehold rights beneath the lands of Surface "
              f"Owner described as {d['legal']}, {d['county']} County, {d['state']} "
              f"({d['acres']} acres); and"),
        ("p", "WHEREAS the parties desire to set forth the terms governing Operator's use of the "
              "surface for drilling and production operations;"),
        ("h2", "Surface Damages (Area of Disturbance)"),
        ("p", f"Operator shall pay Surface Owner {d['surface_payment']} per well pad as initial "
              f"surface damages within the area of disturbance, plus {d['road_payment']} per rod "
              f"for new roads and pipelines, and shall separately compensate for damage to crops, "
              f"livestock, fences, and improvements outside the area of disturbance."),
        ("h2", "Location of Operations and Setbacks"),
        ("p", "Well pads, tank batteries, and central facilities shall be located no closer than "
              "500 feet to any existing residence or water well without the Surface Owner's written "
              "consent. Operator shall consult Surface Owner on locations before staking, fence all "
              "production facilities, and control noxious weeds."),
        ("h2", "Interim and Final Reclamation"),
        ("p", "Operator shall perform interim reclamation of areas not needed during production "
              "(replace topsoil, control weeds, reseed). Upon plugging and abandonment, Operator "
              "shall remove all equipment, recontour to approximate original grade, replace "
              "topsoil, and reseed with an approved mix within twelve (12) months (final "
              "reclamation)."),
        ("h2", "Water and Environmental Protection"),
        ("p", "Operator shall not use fresh water from Surface Owner's wells or ponds without a "
              "separate written agreement; shall take measures to prevent soil erosion and "
              "pollution of land, water, springs, and ponds; and shall not release or discharge "
              "toxic or hazardous substances on the property."),
        ("h2", "Insurance and Indemnity"),
        ("p", "Operator shall maintain commercial general liability and environmental liability "
              "insurance in commercially reasonable amounts and shall indemnify and hold Surface "
              "Owner harmless from claims arising out of Operator's operations on the property."),
        ("blank", ""),
        ("p", f"SURFACE OWNER: {d['lessor']}"),
        ("p", f"OPERATOR: {d['lessee']}"),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_easement(d):
    return [
        ("h1", "Right-of-Way and Pipeline Easement"),
        ("p", f"This Right-of-Way and Easement Agreement is made {d['effective_date']} by "
              f"{d['grantor']} (\"Grantor\") in favor of {d['grantee']} (\"Grantee\")."),
        ("h2", "Grant of Easement"),
        ("p", f"For consideration of {d['consideration']}, Grantor grants Grantee a permanent "
              f"easement {d['perm_width']} feet in width to construct, operate, maintain, inspect, "
              f"replace, and remove one pipeline for the transportation of oil, gas, water, and "
              f"related substances, with appurtenant valves and cathodic protection, together with "
              f"a temporary construction easement {d['temp_width']} feet in width during initial "
              f"installation only."),
        ("h2", "Location"),
        ("p", f"The easement crosses the following land: {d['legal']}, {d['county']} County, "
              f"{d['state']}, the centerline being as staked and as shown on the plat attached as "
              f"Exhibit A. The right-of-way traverses approximately {d['rods']} rods "
              f"({d['acres']} acres of permanent working space)."),
        ("h2", "Depth, Double-Ditching, and Restoration"),
        ("p", "The pipeline shall be buried a minimum of 48 inches below the surface and below the "
              "plow depth of cultivated land. Grantee shall double-ditch so that topsoil is "
              "separated and returned to the surface, repair fences, drainage, and terraces, "
              "re-seed annually until vegetation is re-established, and pay for growing crops and "
              "timber actually damaged."),
        ("h2", "One Pipeline; Term and Abandonment"),
        ("p", "This grant is limited to a single pipeline; any additional line requires separate "
              "consideration. The easement continues so long as used for the purposes granted; "
              "abandonment for twenty-four (24) consecutive months terminates the grant and title "
              "reverts to Grantor."),
        ("blank", ""),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_title_opinion(d):
    return [
        ("h1", "Drilling Title Opinion"),
        ("p", f"TO: {d['lessee']}"),
        ("p", f"FROM: {d['examiner']}"),
        ("p", f"DATE: {d['effective_date']}"),
        ("p", f"RE: {d['legal']}, {d['county']} County, {d['state']} ({d['acres']} acres)"),
        ("blank", ""),
        ("h2", "A. Scope and Materials Examined"),
        ("p", f"At your request, I have examined an abstract of title covering the captioned lands, "
              f"comprising {d['abstract_entries']} numbered entries certified to {d['cert_date']} by "
              f"{d['abstractor']}. This opinion is limited to record title and is rendered for the "
              f"sole use of the addressee."),
        ("h2", "B. Tract Ownership -- Mineral Estate"),
        ("p", f"Tract 1 (the captioned lands). The mineral fee and executive right are owned by "
              f"{d['mineral_owner']}, subject to a non-participating royalty interest (NPRI) of "
              f"{d['npri']} held by {d['npri_owner']}. Total net mineral acres: {d['acres']}."),
        ("h2", "C. Schedule of Leases and Encumbrances"),
        ("p", f"Lease 1: from the mineral owner to {d['lessee']}, royalty {d['lease_royalty']}, "
              f"yielding a net revenue interest to Lessee of {d['nri']}. Encumbrance: a deed of "
              f"trust of record affecting the surface estate only, noted for information."),
        ("h2", "D. Comments"),
        ("p", "Comment 1: The 1987 probate of the Estate of A. Henderson did not include a recorded "
              "order admitting the will; heirship is presumed from the family settlement agreement "
              "at Book 412, Page 88."),
        ("p", "Comment 2: A prior oil and gas lease appears expired by its own terms for lack of "
              "production, but no release of record was located."),
        ("h2", "E. Requirements"),
        ("p", "Requirement 1: Obtain and record a release of the expired lease referenced in "
              "Comment 2, or a stipulation of interest and non-development."),
        ("p", "Requirement 2: Secure recorded affidavits of heirship for the Henderson interest "
              "before disbursing proceeds; suspend the corresponding decimal interest until cured."),
        ("p", "Requirement 3: Confirm payment of ad valorem taxes for the current year; unpaid "
              "taxes constitute a first lien on the property."),
        ("blank", ""),
        ("p", "Respectfully submitted,"),
        ("p", d['examiner']),
    ]


def t_grazing_lease(d):
    return [
        ("h1", "Grazing and Ranch Lease"),
        ("p", f"This Grazing Lease is made {d['effective_date']} between {d['lessor']} (\"Lessor\") "
              f"and {d['lessee']} (\"Lessee\")."),
        ("h2", "Leased Premises"),
        ("p", f"Lessor leases to Lessee for grazing and ranching purposes the following lands: "
              f"{d['legal']}, containing {d['acres']} acres, more or less, in {d['county']} County, "
              f"{d['state']} (the \"Ranch\")."),
        ("h2", "Term and Rent"),
        ("p", f"The term is {d['term']} beginning {d['effective_date']}. Lessee shall pay annual "
              f"rent of {d['rent']}, payable {d['rent_schedule']}."),
        ("h2", "Carrying Capacity"),
        ("p", f"Stocking shall not exceed {d['aum']} animal units; Lessee shall practice rotational "
              f"grazing and shall not overgraze. Lessee maintains fences, gates, stock tanks, and "
              f"windmills in good repair, ordinary wear excepted."),
        ("h2", "Reservations"),
        ("p", "Lessor reserves all oil, gas, and mineral rights and the right to grant surface use "
              "to mineral lessees; Lessee's rent shall be equitably reduced for acreage taken out "
              "of grazing by such operations. Hunting rights are reserved to Lessor."),
        ("blank", ""),
        ("p", f"LESSOR: {d['lessor']}"),
        ("p", f"LESSEE: {d['lessee']}"),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_amendment(d):
    return [
        ("h1", "Amendment and Extension of Oil and Gas Lease"),
        ("p", f"This Amendment and Extension (\"Amendment\") is made {d['effective_date']} between "
              f"{d['lessor']} (\"Lessor\") and {d['lessee']} (\"Lessee\")."),
        ("h2", "Original Lease"),
        ("p", f"Lessor and Lessee are parties to that certain Oil and Gas Lease dated "
              f"{d['orig_date']}, recorded as {d['recording']['instrument_no']} in Book "
              f"{d['recording']['book']}, Page {d['recording']['page']} of the records of "
              f"{d['county']} County, {d['state']} (the \"Lease\"), covering: {d['legal']}, "
              f"{d['acres']} acres."),
        ("h2", "Extension of Primary Term"),
        ("p", f"In consideration of {d['bonus']}, the primary term of the Lease is extended for an "
              f"additional period of {d['term']} from the expiration of the original primary term, "
              f"upon the same terms except as amended below."),
        ("h2", "Amended Royalty"),
        ("p", f"The royalty under the Lease is amended and increased to {d['royalty']} of "
              f"production, effective as to production on and after the date of this Amendment."),
        ("h2", "Ratification"),
        ("p", "Except as expressly amended, all terms of the Lease are ratified and confirmed and "
              "remain in full force and effect. Lessor ratifies and confirms the Lease as amended."),
        ("blank", ""),
        ("p", f"LESSOR: {d['lessor']}"),
        ("p", f"LESSEE: {d['lessee']}"),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_doto(d):
    cl = d.get("county_label", "County")
    return [
        ("h1", "Division Order Title Opinion"),
        ("p", f"TO: {d['operator']}"),
        ("p", f"FROM: {d['examiner']}"),
        ("p", f"DATE: {d['effective_date']}"),
        ("p", f"RE: {d['well']} -- {d['legal']}, {d['county']} {cl}, {d['state']} "
              f"({d['acres']} acres)"),
        ("blank", ""),
        ("h2", "A. Materials Examined"),
        ("p", f"This Division Order Title Opinion supplements the Drilling Title Opinion dated "
              f"{d['drilling_opinion_date']} and covers title from that certification date through "
              f"first production. It is rendered to determine the parties entitled to share in "
              f"production from the captioned well and their decimal interests."),
        ("h2", "B. Division of Interest"),
        ("p", "Subject to the requirements below, proceeds of production should be credited as "
              "follows (decimals total 1.00000000):"),
        ("table", d["division"]),
        ("h2", "C. Requirements"),
        ("p", "Requirement 1: Obtain executed division orders from each owner before releasing "
              "proceeds. Requirement 2: Suspend the decimal shown for any owner whose curative "
              "(affidavit of heirship; recorded probate) is not yet of record."),
        ("blank", ""),
        ("p", "Respectfully submitted,"),
        ("p", d["examiner"]),
    ]


def t_division_order(d):
    return [
        ("h1", "Division Order"),
        ("p", "(NADOA Model Form -- for distribution of proceeds; does not amend the lease)"),
        ("blank", ""),
        ("p", f"Property No.: {d['property_no']}    Effective Date: {d['effective_date']}"),
        ("p", f"Property Name: {d['well']}"),
        ("p", f"Operator / Payor: {d['operator']}"),
        ("p", f"County/State: {d['county']} County, {d['state']}"),
        ("p", f"Legal Description: {d['legal']}"),
        ("h2", "Owners and Decimal Interests"),
        ("table", d["owners"]),
        ("h2", "Terms"),
        ("p", "The undersigned owner certifies ownership of the decimal interest set out above in "
              "production from the property described. The owner agrees that the payor may withhold "
              "payment until ownership is established to the payor's satisfaction, will be notified "
              "before any change of decimal, and will indemnify the payor against adverse claims. "
              "Payment may be suspended for amounts under the minimum-pay threshold. This Division "
              "Order does not amend or ratify any lease."),
        ("blank", ""),
        ("p", "OWNER: ______________________________   Tax ID: ____________   Date: __________"),
    ] + _notary(d["state"], "County", d["county"], "the undersigned owner")


def t_affidavit_heirship(d):
    return [
        ("h1", "Affidavit of Heirship"),
        ("p", f"STATE OF {d['state'].upper()}"),
        ("p", f"COUNTY OF {d['county'].upper()}"),
        ("blank", ""),
        ("p", f"BEFORE ME, the undersigned authority, personally appeared {d['affiant']} "
              f"(\"Affiant\"), a disinterested person of lawful age, who, being duly sworn, deposed "
              f"and stated:"),
        ("p", f"1. I knew {d['decedent']} (\"Decedent\") for many years and have personal knowledge "
              f"of the Decedent's family history. Decedent died {d['date_of_death']} at "
              f"{d['place_of_death']}, {d['testacy']}."),
        ("p", f"2. Marital history: Decedent was married to {d['spouse']}, who {d['spouse_status']}. "
              f"There were no other marriages."),
        ("p", "3. The following are all of the children born to or adopted by Decedent, and there "
              "are no other children, living or deceased, natural, adopted, or pretermitted:"),
        ("table", d["heirs"]),
        ("p", f"4. Decedent owned an interest in the oil, gas, and other minerals described as: "
              f"{d['legal']}, {d['county']} County, {d['state']}."),
        ("p", "5. To my knowledge the estate owed no debts that remain unpaid, and no administration "
              "is pending. The heirs named above are the sole owners of Decedent's said interest in "
              "the proportions shown."),
        ("blank", ""),
        ("p", f"AFFIANT: {d['affiant']}"),
        ("blank", ""),
        ("p", f"Subscribed and sworn to before me on {d['effective_date']}."),
        ("p", "____________________________________"),
        ("p", "Notary Public   My commission expires: ______________"),
    ]


def t_probate_order(d):
    return [
        ("p", f"IN THE {d['court']}"),
        ("p", f"{d['county'].upper()} COUNTY, {d['state'].upper()}"),
        ("blank", ""),
        ("p", f"IN THE MATTER OF THE ESTATE OF {d['decedent'].upper()}, DECEASED."),
        ("p", f"No. {d['cause_no']}"),
        ("blank", ""),
        ("h1", "Order Admitting Will to Probate and Appointing Executor"),
        ("p", f"On {d['effective_date']} came on to be heard the application for probate of the "
              f"written will of {d['decedent']}, Deceased, and the Court, having heard the evidence, "
              f"FINDS:"),
        ("p", f"1. The Decedent died on {d['date_of_death']}, and this Court has jurisdiction and "
              f"venue. 2. The Decedent left a valid written will dated {d['will_date']}, not revoked. "
              f"3. {d['executor']} is named executor in the will and is not disqualified."),
        ("p", "IT IS THEREFORE ORDERED that the will is admitted to probate; that "
              f"{d['executor']} is appointed and shall serve as independent executor without bond; "
              f"and that letters testamentary issue upon qualification according to law."),
        ("p", f"The Decedent's estate includes, among other property, an interest in the oil, gas, "
              f"and minerals described as {d['legal']}, {d['county']} County, {d['state']}."),
        ("blank", ""),
        ("p", f"SIGNED this {d['effective_date']}."),
        ("p", "____________________________________"),
        ("p", "Judge Presiding"),
    ]


def t_assignment_absc(d):
    return [
        ("h1", "Assignment, Bill of Sale and Conveyance"),
        ("p", f"This Assignment, Bill of Sale and Conveyance (\"Assignment\") is made effective "
              f"{d['effective_date']} (the \"Effective Time\"), from {d['assignor']} (\"Assignor\") "
              f"to {d['assignee']} (\"Assignee\")."),
        ("h2", "Granting Clause"),
        ("p", f"For {d['consideration']}, Assignor GRANTS, BARGAINS, SELLS, ASSIGNS, and CONVEYS to "
              f"Assignee all of Assignor's right, title, and interest in and to the oil and gas "
              f"leases described on Exhibit A (the \"Leases\"), together with the lands covered, the "
              f"wells, equipment, and the associated contracts and records (collectively, the "
              f"\"Assets\")."),
        ("p", f"The Assets are situated in {d['county']} {d.get('county_label', 'County')}, "
              f"{d['state']}; the primary tract is described as {d['legal']}."),
        ("h2", "Reserved Override"),
        ("p", f"Assignor RESERVES an overriding royalty interest of {d['orri']} of 8/8ths in all "
              f"production from the Leases, proportionately reduced to Assignor's net interest "
              f"assigned."),
        ("h2", "Exhibit A -- Leases Conveyed"),
        ("table", d["leases"]),
        ("h2", "Warranty and Assumption"),
        ("p", "Assignor conveys the Assets by special warranty of title, by, through, and under "
              "Assignor but not otherwise. Assignee assumes all obligations relating to the Assets "
              "arising from and after the Effective Time."),
        ("blank", ""),
        ("p", f"ASSIGNOR: {d['assignor']}"),
        ("p", f"ASSIGNEE: {d['assignee']}"),
    ] + _notary(d["state"], "County", d["county"], d["assignor"])


def t_joa(d):
    return [
        ("h1", "Joint Operating Agreement"),
        ("p", "(A.A.P.L. Form 610 -- 1989 Model Form Operating Agreement)"),
        ("blank", ""),
        ("p", f"This Operating Agreement is dated {d['effective_date']}, by and among the parties "
              f"who execute it (\"Parties\")."),
        ("p", f"Operator: {d['operator']}."),
        ("p", f"Contract Area: {d['legal']}, {d['county']} County, {d['state']}."),
        ("h2", "Parties and Working Interests"),
        ("table", d["interests"]),
        ("h2", "Article V -- Operator"),
        ("p", f"{d['operator']} is designated Operator and shall conduct operations as a reasonably "
              f"prudent operator, but shall have no liability except for gross negligence or willful "
              f"misconduct. Operator may be removed for cause by the affirmative vote of "
              f"Non-Operators owning a majority of the working interest remaining after excluding "
              f"Operator."),
        ("h2", "Article VI -- Drilling and Development"),
        ("p", f"The Initial Well shall be drilled to a depth sufficient to test the {d['formation']}. "
              f"Any Party may propose subsequent operations; a Party electing not to participate "
              f"(\"non-consent\") relinquishes its share of production until the participating "
              f"Parties recover {d['nonconsent']} of such non-consenting Party's share of costs."),
        ("h2", "Article VII -- Expenditures and Liability"),
        ("p", f"Operator shall furnish an AFE for any single operation estimated to cost more than "
              f"{d['afe_threshold']}. The liability of the Parties is several, not joint or "
              f"collective; each Party is responsible only for its proportionate share, secured by "
              f"the lien and security interest granted in Article VII.B."),
        ("h2", "Article XI / XV -- Force Majeure; Miscellaneous"),
        ("p", "Obligations (other than to make money payments) are suspended during force majeure. "
              "Exhibits A through G (including the Accounting Procedure and Insurance) are attached "
              "and incorporated by reference."),
        ("blank", ""),
        ("p", f"OPERATOR: {d['operator']}"),
    ]


def t_farmout(d):
    return [
        ("h1", "Farmout Agreement"),
        ("p", f"This Farmout Agreement is made {d['effective_date']} between {d['farmor']} "
              f"(\"Farmor\") and {d['farmee']} (\"Farmee\")."),
        ("h2", "Recitals"),
        ("p", f"Farmor owns the oil and gas leasehold covering {d['legal']}, {d['county']} County, "
              f"{d['state']} ({d['acres']} acres), and Farmee desires to earn an assignment by "
              f"drilling."),
        ("h2", "Earning Well"),
        ("p", f"Farmee shall, at its sole cost and risk, commence the Earning Well on or before "
              f"{d['commence_by']} and drill it with due diligence to a depth sufficient to test the "
              f"{d['formation']}, or to {d['depth']} feet, whichever is shallower (casing point)."),
        ("h2", "Earned Acreage and Assignment"),
        ("p", f"Upon completion of the Earning Well as a well capable of production, Farmor shall "
              f"assign to Farmee the leasehold as to the spacing unit for the Earning Well, "
              f"insofar as it covers {d['earned_depths']}."),
        ("h2", "Reserved Override and Back-In"),
        ("p", f"Farmor reserves an overriding royalty of {d['orri']}, convertible at Farmor's "
              f"election after Payout to a {d['backin']} working interest (a back-in), bearing its "
              f"proportionate share of costs thereafter."),
        ("h2", "Continuous Drilling"),
        ("p", "Farmee may earn additional acreage by conducting continuous drilling operations with "
              "no more than 120 days between the completion of one well and the spudding of the next."),
        ("blank", ""),
        ("p", f"FARMOR: {d['farmor']}"),
        ("p", f"FARMEE: {d['farmee']}"),
    ]


def t_ami(d):
    return [
        ("h1", "Area of Mutual Interest Agreement"),
        ("p", f"This Area of Mutual Interest Agreement (\"AMI Agreement\") is made {d['effective_date']} "
              f"between {d['party_a']} and {d['party_b']} (each a \"Party\"), who shall participate "
              f"in the proportions {d['proportions']}."),
        ("h2", "AMI Area"),
        ("p", f"The Area of Mutual Interest comprises the lands within {d['ami_area']}, "
              f"{d['county']} County, {d['state']}."),
        ("h2", "Term"),
        ("p", f"This AMI Agreement remains in effect for {d['term']} from the effective date."),
        ("h2", "Offer of Acquired Interests"),
        ("p", "If, during the term, any Party (the \"Acquiring Party\") acquires any oil and gas "
              "lease, mineral, or leasehold interest within the AMI Area, it shall promptly notify "
              "the other Party and offer it the right to acquire its proportionate share at "
              "proportionate cost. The other Party shall elect in writing within thirty (30) days; "
              "failure to elect is deemed an election not to participate as to that acquisition."),
        ("h2", "Excluded Acquisitions"),
        ("p", "Renewals or extensions of leases existing before the effective date, and interests "
              "acquired through corporate merger, are excluded from this AMI."),
        ("blank", ""),
        ("p", f"PARTY: {d['party_a']}"),
        ("p", f"PARTY: {d['party_b']}"),
    ]


def t_pooling_order(d):
    return [
        ("p", f"BEFORE THE {d['commission'].upper()}"),
        ("blank", ""),
        ("p", f"APPLICANT: {d['applicant']}"),
        ("p", f"RELIEF SOUGHT: Pooling"),
        ("p", f"LEGAL DESCRIPTION: {d['legal']}, {d['county']} County, {d['state']}"),
        ("p", f"CAUSE CD NO. {d['cause_no']}    ORDER NO. {d['order_no']}"),
        ("p", f"DATE OF ORDER: {d['effective_date']}"),
        ("blank", ""),
        ("h1", "Order of the Commission (Pooling)"),
        ("p", f"The Commission FINDS: Applicant owns an interest in the {d['formation']} common "
              f"source of supply underlying the above unit; the owners have not agreed to pool "
              f"voluntarily; and pooling is necessary to avoid the drilling of unnecessary wells, to "
              f"protect correlative rights, and to prevent waste."),
        ("p", "IT IS ORDERED that the interests in the unit are pooled. Each owner who has not "
              "agreed shall, within twenty (20) days of this Order, elect one of the following:"),
        ("table", d["options"]),
        ("p", f"Owners who do not timely elect are deemed to have elected the lowest cash bonus "
              f"option. {d['operator']} is designated Operator of the unit and is authorized to "
              f"drill and operate the unit well."),
        ("blank", ""),
        ("p", "DONE AND PERFORMED by order of the Commission."),
    ]


def t_release_lease(d):
    r = d["orig_recording"]
    return [
        ("h1", "Release of Oil and Gas Lease"),
        ("p", f"KNOW ALL PERSONS: {d['releasor']} (\"Releasor\"), the present owner of the leasehold "
              f"created by the oil and gas lease described below, for valuable consideration, hereby "
              f"RELEASES, SURRENDERS, and QUITCLAIMS unto the present owner of the land all of "
              f"Releasor's right, title, and interest in and to said lease."),
        ("h2", "Lease Released"),
        ("p", f"That certain Oil and Gas Lease dated {d['orig_date']}, from {d['orig_lessor']}, as "
              f"Lessor, recorded as Instrument No. {r['instrument_no']} in Book {r['book']}, Page "
              f"{r['page']} of the records of {d['county']} County, {d['state']}, covering: "
              f"{d['legal']}."),
        ("p", "Said lease is hereby terminated and held for naught, and the land is released from "
              "all obligations thereunder, effective as of the date below."),
        ("blank", ""),
        ("p", f"EXECUTED {d['effective_date']}."),
        ("p", f"RELEASOR: {d['releasor']}"),
    ] + _notary(d["state"], "County", d["county"], d["releasor"])


def t_ratification(d):
    r = d["orig_recording"]
    return [
        ("h1", "Ratification of Oil and Gas Lease"),
        ("p", f"The undersigned, {d['owner']} (\"Owner\"), for valuable consideration, hereby "
              f"RATIFIES, ADOPTS, and CONFIRMS that certain Oil and Gas Lease dated {d['orig_date']}, "
              f"recorded as Instrument No. {r['instrument_no']} in Book {r['book']}, Page {r['page']} "
              f"of the records of {d['county']} County, {d['state']} (the \"Lease\"), covering: "
              f"{d['legal']}."),
        ("h2", "Lease and Grant of Interest"),
        ("p", f"Owner adopts the Lease as fully as if Owner had originally executed it, leases and "
              f"lets Owner's interest in the described lands to the Lessee under the Lease upon its "
              f"terms (royalty {d['royalty']}), and joins in and consents to the Lease's pooling and "
              f"unitization provisions as to Owner's interest."),
        ("p", "This Ratification is effective as to Owner's interest and does not impair the Lease "
              "as to any other interest."),
        ("blank", ""),
        ("p", f"EXECUTED {d['effective_date']}."),
        ("p", f"OWNER: {d['owner']}"),
    ] + _notary(d["state"], "County", d["county"], d["owner"])


def t_afe(d):
    return [
        ("h1", "Authority for Expenditure (AFE)"),
        ("p", f"AFE No.: {d['afe_no']}    Date: {d['effective_date']}    Type: Drill and Complete"),
        ("p", f"Operator: {d['operator']}"),
        ("p", f"Well: {d['well']}"),
        ("p", f"Location: {d['legal']}, {d['county']} County, {d['state']}"),
        ("p", f"Objective Depth: {d['depth']} feet ({d['formation']})    Estimated Spud: "
              f"{d['spud']}"),
        ("h2", "Estimated Costs (USD)"),
        ("table", d["costs"]),
        ("p", "Intangible drilling costs (IDC) are generally deductible as incurred; tangible "
              "equipment is capitalized and depreciated. This AFE is an estimate; actual costs may "
              "vary. Approval authorizes Operator to incur the costs above for the joint account."),
        ("blank", ""),
        ("p", "APPROVED -- OPERATOR: ______________________   Working Interest: ____________"),
        ("p", "APPROVED -- NON-OPERATOR: __________________   Working Interest: ____________"),
    ]


TEMPLATES = {
    "oil_gas_lease": t_oil_gas_lease, "paidup_modern": t_paidup_modern,
    "metes_bounds_lease": t_metes_bounds_lease, "memorandum": t_memorandum,
    "mineral_deed": t_mineral_deed, "royalty_deed": t_royalty_deed,
    "warranty_deed": t_warranty_deed, "quitclaim": t_quitclaim,
    "surface_use": t_surface_use, "easement": t_easement,
    "title_opinion": t_title_opinion, "grazing_lease": t_grazing_lease,
    "amendment": t_amendment,
    "doto": t_doto, "division_order": t_division_order,
    "affidavit_heirship": t_affidavit_heirship, "probate_order": t_probate_order,
    "assignment_absc": t_assignment_absc, "joa": t_joa, "farmout": t_farmout,
    "ami": t_ami, "pooling_order": t_pooling_order, "release_lease": t_release_lease,
    "ratification": t_ratification, "afe": t_afe,
}

# ---------------------------------------------------------------------------
# The corpus: 24 synthetic documents.
#   - PLSS docs carry a "plss" dict; legal text AND lat/long are derived from it,
#     and "town" is the real county-seat used only to sanity-check the centroid.
#   - Non-PLSS docs (Texas abstract, metes-and-bounds, land grant) carry their
#     own "legal" + "lat"/"lon" (county/town approximate) + "system".
# ---------------------------------------------------------------------------

DOCS = [
    {"id": "01-ogl-midland-tx", "template": "oil_gas_lease", "doc_type": "Oil and Gas Lease",
     "form_no": "TX-88-PB", "state": "Texas", "county": "Midland", "town": "Midland",
     "lat": 31.9973, "lon": -102.0779, "system": "Texas abstract/block-section",
     "legal": "Section 14, Block 39, T-2-S, Texas & Pacific Ry. Co. Survey, Abstract No. 1187",
     "acres": "640.00", "lessor": "Margaret A. Caldwell, a single woman",
     "lessee": "Llano Estacado Operating, LLC", "effective_date": "January 15, 2025",
     "term": "three (3) years", "royalty": "one-fourth (1/4)",
     "bonus": "$1,500.00 per net mineral acre", "shut_in": "$50.00"},

    {"id": "02-ogl-reeves-tx", "template": "paidup_modern", "doc_type": "Oil and Gas Lease",
     "state": "Texas", "county": "Reeves", "town": "Pecos",
     "lat": 31.4229, "lon": -103.4932, "system": "Texas abstract/block-section",
     "legal": "Section 22, Block 13, H&GN RR Co. Survey, Abstract No. 2204", "acres": "320.00",
     "lessor": "The Holloway Family Trust dated June 3, 1998",
     "lessee": "Delaware Basin Resources, LP", "effective_date": "March 1, 2025",
     "term": "five (5) years", "royalty": "22.5% (9/40)",
     "bonus": "$2,250.00 per net mineral acre"},

    {"id": "03-ogl-lea-nm", "template": "oil_gas_lease", "doc_type": "Oil and Gas Lease",
     "form_no": "NM-PB-01", "state": "New Mexico", "county": "Lea", "town": "Lovington",
     "town_lat": 32.9445, "town_lon": -103.3486,
     "plss": {"mer": "NMPM", "twp": 20, "twp_dir": "S", "rng": 37, "rng_dir": "E",
              "sec": 16, "aliquot": "SE/4"},
     "acres": "160.00", "lessor": "James R. and Linda S. Whitaker, husband and wife",
     "lessee": "Mesa Verde Resources, LP", "effective_date": "February 10, 2025",
     "term": "three (3) years", "royalty": "three-sixteenths (3/16)",
     "bonus": "$1,000.00 per net mineral acre", "shut_in": "$25.00"},

    {"id": "04-ogl-eddy-nm", "template": "oil_gas_lease", "doc_type": "Oil and Gas Lease",
     "form_no": "NM-PB-02", "state": "New Mexico", "county": "Eddy", "town": "Carlsbad",
     "town_lat": 32.4207, "town_lon": -104.2288,
     "plss": {"mer": "NMPM", "twp": 22, "twp_dir": "S", "rng": 28, "rng_dir": "E",
              "sec": 9, "aliquot": "N/2"},
     "acres": "320.00", "lessor": "Pecos Valley Land Company, a New Mexico corporation",
     "lessee": "Mesa Verde Resources, LP", "effective_date": "April 22, 2025",
     "term": "five (5) years", "royalty": "one-fifth (1/5)",
     "bonus": "$1,750.00 per net mineral acre", "shut_in": "$50.00"},

    {"id": "05-ogl-mckenzie-nd", "template": "paidup_modern", "doc_type": "Oil and Gas Lease",
     "state": "North Dakota", "county": "McKenzie", "town": "Watford City",
     "town_lat": 47.8022, "town_lon": -103.2832,
     "plss": {"mer": "5th", "twp": 150, "twp_dir": "N", "rng": 98, "rng_dir": "W",
              "sec": 22, "aliquot": "S/2"},
     "acres": "320.00", "lessor": "Arnold T. Bergstrom and Carol J. Bergstrom, as joint tenants",
     "lessee": "Bakken Ridge Energy, Inc.", "effective_date": "May 5, 2024",
     "term": "five (5) years", "royalty": "18.75% (3/16)",
     "bonus": "$900.00 per net mineral acre"},

    {"id": "06-ogl-weld-co", "template": "paidup_modern", "doc_type": "Oil and Gas Lease",
     "state": "Colorado", "county": "Weld", "town": "Greeley",
     "town_lat": 40.4233, "town_lon": -104.7091,
     "plss": {"mer": "6th", "twp": 6, "twp_dir": "N", "rng": 63, "rng_dir": "W",
              "sec": 9, "aliquot": "SW/4"},
     "acres": "160.00", "lessor": "Front Range Cattle Co., LLC",
     "lessee": "Front Range Petroleum, LLC", "effective_date": "September 12, 2024",
     "term": "three (3) years", "royalty": "20% (1/5)",
     "bonus": "$1,200.00 per net mineral acre"},

    {"id": "07-ogl-kingfisher-ok", "template": "oil_gas_lease", "doc_type": "Oil and Gas Lease",
     "form_no": "OK-88", "state": "Oklahoma", "county": "Kingfisher", "town": "Kingfisher",
     "town_lat": 35.8620, "town_lon": -97.9320,
     "plss": {"mer": "IM", "twp": 16, "twp_dir": "N", "rng": 7, "rng_dir": "W",
              "sec": 19, "aliquot": "NE/4"},
     "acres": "160.00",
     "lessor": "Estate of Harlan W. Dietrich, by Susan Dietrich, Personal Representative",
     "lessee": "Red River Minerals, LLC", "effective_date": "July 8, 2024",
     "term": "three (3) years", "royalty": "three-sixteenths (3/16)",
     "bonus": "$800.00 per net mineral acre", "shut_in": "$25.00"},

    {"id": "08-ogl-washington-pa", "template": "metes_bounds_lease",
     "doc_type": "Oil and Gas Lease", "state": "Pennsylvania", "county": "Washington",
     "town": "Amwell Township", "township": "Amwell Township",
     "lat": 40.1742, "lon": -80.2462, "system": "metes and bounds",
     "legal": ("BEGINNING at an iron pin at the northwest corner of lands now or formerly of "
               "Reynolds; thence S 82 deg E 1,420 feet to a post; thence S 6 deg W 3,160 feet "
               "along lands of Maple Run to a stone; thence N 84 deg W 1,390 feet to a white oak; "
               "thence N 5 deg E 3,090 feet to the place of beginning"),
     "acres": "112.40", "source_deed": "Deed Book 1123, Page 456",
     "lessor": "Robert E. Stanton and Patricia Stanton, his wife",
     "lessee": "Keystone Shale Partners, LP", "effective_date": "October 1, 2024",
     "term": "five (5) years", "royalty": "one-eighth (1/8)", "oil_royalty": "one-eighth (1/8)",
     "bonus": "$3,000.00 per net acre"},

    {"id": "09-ogl-greene-pa", "template": "metes_bounds_lease", "doc_type": "Oil and Gas Lease",
     "state": "Pennsylvania", "county": "Greene", "town": "Morgan Township",
     "township": "Morgan Township", "lat": 39.8962, "lon": -80.1811, "system": "metes and bounds",
     "legal": ("BEGINNING at a fence post corner; thence along Township Road 388 N 71 deg E "
               "980 feet; thence S 19 deg E 2,610 feet to a marked hickory; thence S 70 deg W "
               "1,005 feet; thence N 18 deg W 2,640 feet to the point of beginning"),
     "acres": "61.80", "source_deed": "Instrument No. 2016-004412",
     "lessor": "Greene Hills Land Holdings, LLC", "lessee": "Keystone Shale Partners, LP",
     "effective_date": "November 14, 2024", "term": "five (5) years",
     "royalty": "fifteen percent (15%)", "oil_royalty": "one-eighth (1/8)",
     "bonus": "$2,500.00 per net acre"},

    {"id": "10-ogl-doddridge-wv", "template": "metes_bounds_lease", "doc_type": "Oil and Gas Lease",
     "state": "West Virginia", "county": "Doddridge", "town": "McClellan District",
     "township": "McClellan District", "lat": 39.2690, "lon": -80.7762,
     "system": "metes and bounds",
     "legal": ("Beginning at a sugar maple on the bank of Middle Island Creek; thence with the "
               "creek N 44 deg E 62 poles; thence leaving the creek S 60 deg E 138 poles to a "
               "stone; thence S 40 deg W 70 poles; thence N 58 deg W 142 poles to the beginning"),
     "acres": "88.25", "source_deed": "Deed Book 244, Page 19",
     "lessor": "Floyd and Wanda Carpenter, husband and wife", "lessee": "Allegheny Gas Company",
     "effective_date": "August 19, 2024", "term": "five (5) years",
     "royalty": "one-eighth (1/8)", "oil_royalty": "one-eighth (1/8)",
     "bonus": "$1,800.00 per net acre"},

    {"id": "11-ogl-belmont-oh", "template": "oil_gas_lease", "doc_type": "Oil and Gas Lease",
     "form_no": "OH-PB", "state": "Ohio", "county": "Belmont", "town": "Mead Township",
     "lat": 40.0801, "lon": -80.9009, "system": "metes and bounds (sectionalized)",
     "legal": "Situate in Mead Township, Section 18, being 87.60 acres out of a 160-acre original tract",
     "acres": "87.60", "lessor": "The Novak Revocable Living Trust",
     "lessee": "Buckeye Utica Operating, LLC", "effective_date": "June 3, 2024",
     "term": "five (5) years", "royalty": "one-sixth (1/6)", "bonus": "$4,000.00 per net acre",
     "shut_in": "$100.00"},

    {"id": "12-ogl-caddo-la", "template": "oil_gas_lease", "doc_type": "Oil and Gas Lease",
     "form_no": "LA-BR", "state": "Louisiana", "county": "Caddo", "county_label": "Parish",
     "town": "Shreveport", "town_lat": 32.5252, "town_lon": -93.7502,
     "plss": {"mer": "LA", "twp": 18, "twp_dir": "N", "rng": 15, "rng_dir": "W",
              "sec": 24, "aliquot": "all"},
     "acres": "80.00", "lessor": "Beaulieu Land & Timber, L.L.C.", "lessee": "Caddo Pine Energy, LLC",
     "effective_date": "December 2, 2024", "term": "three (3) years", "royalty": "one-fourth (1/4)",
     "bonus": "$600.00 per net mineral acre", "shut_in": "$50.00"},

    {"id": "13-ogl-campbell-wy", "template": "paidup_modern", "doc_type": "Oil and Gas Lease",
     "state": "Wyoming", "county": "Campbell", "town": "Gillette",
     "town_lat": 44.2911, "town_lon": -105.5022,
     "plss": {"mer": "6th", "twp": 49, "twp_dir": "N", "rng": 71, "rng_dir": "W",
              "sec": 5, "aliquot": "Lots 3 and 4, S/2 NW/4"},
     "acres": "158.40", "lessor": "Powder River Grazing Association",
     "lessee": "Powder River Resources, LLC", "effective_date": "April 1, 2024",
     "term": "five (5) years", "royalty": "one-sixth (1/6)",
     "bonus": "$350.00 per net mineral acre"},

    {"id": "14-ogl-kern-ca", "template": "oil_gas_lease", "doc_type": "Oil and Gas Lease",
     "form_no": "CA-88", "state": "California", "county": "Kern", "town": "Bakersfield",
     "town_lat": 35.3733, "town_lon": -119.0187,
     "plss": {"mer": "MDM", "twp": 30, "twp_dir": "S", "rng": 28, "rng_dir": "E",
              "sec": 12, "aliquot": "NW/4"},
     "acres": "160.00", "lessor": "San Joaquin Heritage Farms, Inc.",
     "lessee": "San Joaquin Oil Company", "effective_date": "February 28, 2025",
     "term": "three (3) years", "royalty": "one-sixth (1/6)",
     "bonus": "$1,100.00 per net mineral acre", "shut_in": "$50.00"},

    {"id": "15-memo-karnes-tx", "template": "memorandum",
     "doc_type": "Memorandum of Oil and Gas Lease", "state": "Texas", "county": "Karnes",
     "town": "Karnes City", "lat": 28.8853, "lon": -97.9003,
     "system": "Texas abstract/block-section", "legal": "J. de la Garza Survey, Abstract No. 456",
     "acres": "210.50", "lessor": "Esperanza Ranch Partners, Ltd.",
     "lessee": "Eagle Ford Operating Company", "effective_date": "January 9, 2025",
     "term": "three (3) years", "royalty": "one-fourth (1/4)",
     "recording": {"instrument_no": "2025-0000487", "book": "1042", "page": "330",
                   "recorded": "January 14, 2025", "recorder": "Karnes County Clerk"}},

    {"id": "16-mineral-deed-stephens-ok", "template": "mineral_deed", "doc_type": "Mineral Deed",
     "state": "Oklahoma", "county": "Stephens", "town": "Duncan",
     "town_lat": 34.5023, "town_lon": -97.9578,
     "plss": {"mer": "IM", "twp": 1, "twp_dir": "S", "rng": 6, "rng_dir": "W",
              "sec": 27, "aliquot": "SW/4"},
     "acres": "160.00", "interest": "one-half (1/2)", "warranty": "general",
     "grantor": "Dorothy M. Albright, a widow", "grantee": "Chisholm Trail Royalties, LLC",
     "consideration": "Ten Dollars and other good and valuable consideration",
     "effective_date": "March 18, 2025"},

    {"id": "17-royalty-deed-reagan-tx", "template": "royalty_deed", "doc_type": "Royalty Deed",
     "state": "Texas", "county": "Reagan", "town": "Big Lake", "lat": 31.1932, "lon": -101.4663,
     "system": "Texas abstract/block-section",
     "legal": "Section 7, Block 2, H&TC RR Co. Survey, Abstract No. 89", "acres": "640.00",
     "interest": "a 1/32 of 8/8", "grantor": "University Lands Heritage Trust",
     "grantee": "Santa Rita Royalty Company", "consideration": "$48,000.00",
     "effective_date": "May 30, 2025"},

    {"id": "18-warranty-deed-garfield-co", "template": "warranty_deed",
     "doc_type": "General Warranty Deed", "state": "Colorado", "county": "Garfield",
     "town": "Glenwood Springs", "town_lat": 39.5505, "town_lon": -107.3248,
     "plss": {"mer": "6th", "twp": 6, "twp_dir": "S", "rng": 92, "rng_dir": "W",
              "sec": 14, "aliquot": "NE/4 SE/4"},
     "acres": "40.00", "reservation": "one-half (1/2)", "grantor": "Roaring Fork Holdings, LLC",
     "grantee": "Caleb and Marie Donnelly, as joint tenants", "consideration": "$385,000.00",
     "effective_date": "April 11, 2025"},

    {"id": "19-quitclaim-rio-arriba-nm", "template": "quitclaim", "doc_type": "Quitclaim Deed",
     "state": "New Mexico", "county": "Rio Arriba", "town": "Tierra Amarilla",
     "lat": 36.7045, "lon": -106.5464, "system": "Spanish/Mexican land grant (tract)",
     "legal": ("A portion of the Tierra Amarilla Land Grant, Tract 7-B, as shown on the amended "
               "grant plat of record"), "acres": "35.75", "grantor": "Ramon C. Trujillo",
     "grantee": "The Trujillo Family, LLC",
     "consideration": "$10.00 and natural love and affection", "effective_date": "June 1, 2025"},

    {"id": "20-surface-use-dunn-nd", "template": "surface_use",
     "doc_type": "Surface Use and Damage Agreement", "state": "North Dakota", "county": "Dunn",
     "town": "Killdeer", "town_lat": 47.3722, "town_lon": -102.7521,
     "plss": {"mer": "5th", "twp": 146, "twp_dir": "N", "rng": 95, "rng_dir": "W",
              "sec": 8, "aliquot": "N/2"},
     "acres": "320.00", "lessor": "Knutson Brothers Farm Partnership",
     "lessee": "Bakken Ridge Energy, Inc.", "effective_date": "July 15, 2024",
     "surface_payment": "$25,000.00", "road_payment": "$30.00"},

    {"id": "21-easement-dewitt-tx", "template": "easement",
     "doc_type": "Right-of-Way and Pipeline Easement", "state": "Texas", "county": "DeWitt",
     "town": "Cuero", "lat": 29.0938, "lon": -97.2886, "system": "Texas abstract/block-section",
     "legal": "out of the William Ponton Survey, Abstract No. 36", "acres": "4.20",
     "perm_width": "30", "temp_width": "50", "rods": "402", "grantor": "Cuero Creek Ranch, Ltd.",
     "grantee": "Guadalupe Midstream Partners, LP", "consideration": "$60,300.00 ($150.00 per rod)",
     "effective_date": "August 4, 2024"},

    {"id": "22-title-opinion-loving-tx", "template": "title_opinion",
     "doc_type": "Drilling Title Opinion", "state": "Texas", "county": "Loving", "town": "Mentone",
     "lat": 31.7060, "lon": -103.5977, "system": "Texas abstract/block-section",
     "legal": "Section 30, Block C-24, PSL Survey, Abstract No. 612", "acres": "640.00",
     "lessee": "Delaware Basin Resources, LP",
     "examiner": "T. Lindqvist, Attorney at Law, Lindqvist & Reyes PLLC",
     "abstractor": "Trans-Pecos Abstract Co.", "abstract_entries": "63",
     "cert_date": "March 31, 2025", "mineral_owner": "Mentone Minerals, Ltd.",
     "npri": "1/8 of 8/8", "npri_owner": "the Henderson Family",
     "lease_royalty": "25% (1/4)", "nri": "0.65625", "effective_date": "April 28, 2025"},

    {"id": "23-grazing-lease-carbon-mt", "template": "grazing_lease",
     "doc_type": "Grazing and Ranch Lease", "state": "Montana", "county": "Carbon",
     "town": "Red Lodge", "town_lat": 45.1863, "town_lon": -109.2466,
     "plss": {"mer": "MPM", "twp": 7, "twp_dir": "S", "rng": 20, "rng_dir": "E",
              "sec": [4, 9], "aliquot": "all"},
     "acres": "1,280.00", "lessor": "Beartooth Mountain Land Trust",
     "lessee": "Rock Creek Cattle Company", "effective_date": "March 1, 2025",
     "term": "five (5) years", "rent": "$18.00 per acre",
     "rent_schedule": "annually in advance on March 1", "aum": "260"},

    {"id": "24-amendment-lea-nm", "template": "amendment",
     "doc_type": "Lease Amendment and Extension", "state": "New Mexico", "county": "Lea",
     "town": "Hobbs", "town_lat": 32.7026, "town_lon": -103.1360,
     "plss": {"mer": "NMPM", "twp": 19, "twp_dir": "S", "rng": 38, "rng_dir": "E",
              "sec": 33, "aliquot": "W/2"},
     "acres": "320.00", "lessor": "James R. and Linda S. Whitaker, husband and wife",
     "lessee": "Mesa Verde Resources, LP", "orig_date": "February 10, 2022",
     "effective_date": "February 1, 2025", "term": "two (2) years", "royalty": "one-fourth (1/4)",
     "bonus": "$500.00 per net mineral acre",
     "recording": {"instrument_no": "2022-0001998", "book": "880", "page": "145"}},

    # --- Batch 2: title/curative, division-order, contracts, regulatory, AFE ---
    # Several are deliberately cross-linked to Batch-1 docs (same tract or estate)
    # to exercise corpus-wide retrieval, and two (division order, AFE) are tabular.

    {"id": "25-doto-reeves-tx", "template": "doto",
     "doc_type": "Division Order Title Opinion", "state": "Texas", "county": "Reeves",
     "town": "Pecos", "lat": 31.4229, "lon": -103.4932, "system": "Texas abstract/block-section",
     "legal": "Section 22, Block 13, H&GN RR Co. Survey, Abstract No. 2204", "acres": "320.00",
     "operator": "Delaware Basin Resources, LP", "well": "Holloway 13-22 #1H",
     "examiner": "T. Lindqvist, Attorney at Law, Lindqvist & Reyes PLLC",
     "drilling_opinion_date": "March 31, 2025", "effective_date": "September 2, 2025",
     "division": [["Owner", "Interest Type", "Decimal"],
                  ["The Holloway Family Trust", "Royalty (RI)", "0.22500000"],
                  ["Big Bend Royalty Partners", "ORRI", "0.02500000"],
                  ["Delaware Basin Resources, LP", "Working Interest (NRI)", "0.75000000"]]},

    {"id": "26-division-order-weld-co", "template": "division_order",
     "doc_type": "Division Order", "state": "Colorado", "county": "Weld", "town": "Greeley",
     "town_lat": 40.4233, "town_lon": -104.7091,
     "plss": {"mer": "6th", "twp": 6, "twp_dir": "N", "rng": 63, "rng_dir": "W",
              "sec": 9, "aliquot": "SW/4"},
     "operator": "Front Range Petroleum, LLC", "property_no": "CO-0096-09",
     "well": "Cattle Co. 9-6 #2", "effective_date": "June 1, 2025",
     "owners": [["Owner No.", "Owner Name", "Type", "Decimal Interest"],
                ["0001", "Front Range Cattle Co., LLC", "RI", "0.16000000"],
                ["0002", "Greeley Mineral Trust", "RI", "0.04000000"],
                ["0100", "Front Range Petroleum, LLC", "WI", "0.80000000"]]},

    {"id": "27-affidavit-heirship-loving-tx", "template": "affidavit_heirship",
     "doc_type": "Affidavit of Heirship", "state": "Texas", "county": "Loving", "town": "Mentone",
     "lat": 31.7060, "lon": -103.5977, "system": "Texas abstract/block-section",
     "legal": "Section 30, Block C-24, PSL Survey, Abstract No. 612", "acres": "640.00",
     "affiant": "Wilbur K. Hayes, a disinterested party",
     "decedent": "Andrew J. Henderson", "date_of_death": "March 2, 2019",
     "place_of_death": "Pecos, Texas", "testacy": "leaving a written will",
     "spouse": "Helen R. Henderson", "spouse_status": "predeceased him on July 9, 2015",
     "effective_date": "April 10, 2025",
     "heirs": [["Name", "Relationship", "Share of Decedent's Interest"],
               ["Carl A. Henderson", "Son", "1/3"],
               ["Diane Henderson Pruitt", "Daughter", "1/3"],
               ["Children of Mark Henderson (predeceased)", "Grandchildren, per stirpes", "1/3"]]},

    {"id": "28-probate-order-loving-tx", "template": "probate_order",
     "doc_type": "Order Admitting Will to Probate", "state": "Texas", "county": "Loving",
     "town": "Mentone", "lat": 31.7060, "lon": -103.5977,
     "system": "Texas abstract/block-section",
     "legal": "Section 30, Block C-24, PSL Survey, Abstract No. 612", "acres": "640.00",
     "court": "COUNTY COURT", "decedent": "Andrew J. Henderson", "cause_no": "P-1042",
     "date_of_death": "March 2, 2019", "will_date": "October 18, 2016",
     "executor": "Carl A. Henderson", "effective_date": "May 6, 2019"},

    {"id": "29-assignment-absc-midland-tx", "template": "assignment_absc",
     "doc_type": "Assignment, Bill of Sale and Conveyance", "state": "Texas", "county": "Midland",
     "town": "Midland", "lat": 31.9973, "lon": -102.0779, "system": "Texas abstract/block-section",
     "legal": ("Section 14, Block 39, T-2-S, T&P RR Co. Survey, Abstract No. 1187 (and additional "
               "leases per Exhibit A)"), "acres": "960.00",
     "assignor": "Llano Estacado Operating, LLC", "assignee": "Permian Acquisition Partners, LP",
     "consideration": "$10.00 and other good and valuable consideration", "orri": "2.0%",
     "effective_date": "July 1, 2025",
     "leases": [["Lease (Lessor)", "County", "Legal Description", "Recording"],
                ["Margaret A. Caldwell", "Midland, TX", "Sec 14, Blk 39, T&P A-1187", "Vol 1180/Pg 22"],
                ["The Holloway Family Trust", "Reeves, TX", "Sec 22, Blk 13, H&GN A-2204", "Vol 905/Pg 410"]]},

    {"id": "30-joa-mckenzie-nd", "template": "joa", "doc_type": "Joint Operating Agreement",
     "state": "North Dakota", "county": "McKenzie", "town": "Watford City",
     "town_lat": 47.8022, "town_lon": -103.2832,
     "plss": {"mer": "5th", "twp": 150, "twp_dir": "N", "rng": 98, "rng_dir": "W",
              "sec": 22, "aliquot": "all"},
     "operator": "Bakken Ridge Energy, Inc.", "effective_date": "April 15, 2024",
     "formation": "Bakken and Three Forks formations", "nonconsent": "300%",
     "afe_threshold": "$50,000.00",
     "interests": [["Party", "Working Interest"],
                   ["Bakken Ridge Energy, Inc. (Operator)", "60.00%"],
                   ["Missouri River Oil, LLC", "25.00%"],
                   ["Dakota Royalty & Working, LP", "15.00%"]]},

    {"id": "31-farmout-eddy-nm", "template": "farmout", "doc_type": "Farmout Agreement",
     "state": "New Mexico", "county": "Eddy", "town": "Carlsbad",
     "town_lat": 32.4207, "town_lon": -104.2288,
     "plss": {"mer": "NMPM", "twp": 22, "twp_dir": "S", "rng": 28, "rng_dir": "E",
              "sec": 9, "aliquot": "N/2"},
     "acres": "320.00", "farmor": "Pecos Valley Land Company",
     "farmee": "Mesa Verde Resources, LP", "commence_by": "December 31, 2025",
     "formation": "Bone Spring formation", "depth": "11,500",
     "earned_depths": "from the surface to the base of the Bone Spring formation",
     "orri": "3.5%", "backin": "25%", "effective_date": "August 1, 2025"},

    {"id": "32-ami-karnes-tx", "template": "ami",
     "doc_type": "Area of Mutual Interest Agreement", "state": "Texas", "county": "Karnes",
     "town": "Karnes City", "lat": 28.8853, "lon": -97.9003,
     "system": "Texas abstract/block-section",
     "legal": "J. de la Garza Survey, Abstract No. 456, and adjoining surveys", "acres": "n/a",
     "party_a": "Eagle Ford Operating Company", "party_b": "Gulf Coast Minerals, LLC",
     "proportions": "50% / 50%",
     "ami_area": "the J. de la Garza Survey (A-456) and all surveys adjoining it",
     "term": "three (3) years", "effective_date": "February 1, 2025"},

    {"id": "33-pooling-order-kingfisher-ok", "template": "pooling_order",
     "doc_type": "Pooling Order", "state": "Oklahoma", "county": "Kingfisher", "town": "Kingfisher",
     "town_lat": 35.8620, "town_lon": -97.9320,
     "plss": {"mer": "IM", "twp": 16, "twp_dir": "N", "rng": 7, "rng_dir": "W",
              "sec": 19, "aliquot": "all"},
     "commission": "Oklahoma Corporation Commission", "applicant": "Red River Minerals, LLC",
     "operator": "Red River Minerals, LLC", "formation": "Mississippian",
     "cause_no": "CD 2024-001234", "order_no": "765432", "effective_date": "September 18, 2024",
     "options": [["Election Option", "Cash Bonus / Net Acre", "Royalty"],
                 ["(a) Participate (share of est. well cost $3,400,000)", "n/a", "n/a"],
                 ["(b) Cash bonus plus royalty", "$1,000", "3/16"],
                 ["(c) Higher royalty, lower bonus", "$200", "1/5"]]},

    {"id": "34-release-loving-tx", "template": "release_lease",
     "doc_type": "Release of Oil and Gas Lease", "state": "Texas", "county": "Loving",
     "town": "Mentone", "lat": 31.7060, "lon": -103.5977,
     "system": "Texas abstract/block-section",
     "legal": "Section 30, Block C-24, PSL Survey, Abstract No. 612", "acres": "640.00",
     "releasor": "Permian Legacy Exploration, Inc.", "orig_lessor": "Mentone Minerals, Ltd.",
     "orig_date": "June 1, 2012", "effective_date": "May 12, 2025",
     "orig_recording": {"instrument_no": "2012-0000311", "book": "188", "page": "540"}},

    {"id": "35-ratification-lea-nm", "template": "ratification",
     "doc_type": "Ratification of Oil and Gas Lease", "state": "New Mexico", "county": "Lea",
     "town": "Lovington", "town_lat": 32.9445, "town_lon": -103.3486,
     "plss": {"mer": "NMPM", "twp": 20, "twp_dir": "S", "rng": 37, "rng_dir": "E",
              "sec": 16, "aliquot": "SE/4"},
     "owner": "Whitaker Family Mineral Trust", "orig_date": "February 10, 2025",
     "royalty": "three-sixteenths (3/16)", "effective_date": "March 20, 2025",
     "orig_recording": {"instrument_no": "2025-0000922", "book": "1011", "page": "77"}},

    {"id": "36-afe-mckenzie-nd", "template": "afe",
     "doc_type": "Authority for Expenditure (AFE)", "state": "North Dakota", "county": "McKenzie",
     "town": "Watford City", "town_lat": 47.8022, "town_lon": -103.2832,
     "plss": {"mer": "5th", "twp": 150, "twp_dir": "N", "rng": 98, "rng_dir": "W",
              "sec": 22, "aliquot": "all"},
     "operator": "Bakken Ridge Energy, Inc.", "afe_no": "ND-2024-0590",
     "well": "Bergstrom 22-150 #1H", "depth": "10,600 TVD / 21,000 MD",
     "formation": "Middle Bakken", "spud": "June 2024", "effective_date": "April 20, 2024",
     "costs": [["Cost Item", "Dry Hole ($)", "Completion ($)"],
               ["Location, roads, pad", "250,000", "0"],
               ["Drilling rig & tools", "3,200,000", "0"],
               ["Drilling fluids & chemicals", "680,000", "0"],
               ["Cementing", "420,000", "150,000"],
               ["Logging, testing, supervision", "510,000", "240,000"],
               ["Surface & intermediate casing", "640,000", "0"],
               ["Production casing & tubing", "0", "1,250,000"],
               ["Wellhead & tree", "0", "180,000"],
               ["Hydraulic fracturing", "0", "4,800,000"],
               ["Facilities & tank battery", "0", "520,000"],
               ["Contingency (10%)", "570,000", "714,000"],
               ["TOTAL ESTIMATE", "6,270,000", "7,854,000"]]},
]


def _first(d: dict, keys):
    """First non-empty value among keys -- party roles vary by instrument type."""
    return next((d[k] for k in keys if d.get(k)), None)


def preprocess(d: dict) -> dict:
    """Derive legal text + coordinates for PLSS docs; compute provenance fields."""
    if "plss" in d:
        d["legal"] = plss_legal(d["plss"])
        lat, lon = plss_centroid(d["plss"])
        d["lat"], d["lon"] = lat, lon
        d["system"] = f"PLSS ({MERIDIANS[d['plss']['mer']][2]})"
        d["coordinate_basis"] = "PLSS tract centroid (computed from township-range-section)"
        d["principal_meridian"] = MERIDIANS[d["plss"]["mer"]][2]
        d["dist_to_town_mi"] = haversine_mi((lat, lon), (d["town_lat"], d["town_lon"]))
    else:
        d["coordinate_basis"] = "county/town approximate"
        d["principal_meridian"] = None
        d["dist_to_town_mi"] = None
    return d


def render_all() -> None:
    LEASES_DIR.mkdir(parents=True, exist_ok=True)
    manifest = []
    print(f"{'id':32} {'coord basis':14} {'centroid':>20}  dist-to-seat")
    for d in map(preprocess, DOCS):
        blocks = TEMPLATES[d["template"]](d)
        (LEASES_DIR / f"{d['id']}.md").write_text(to_markdown(blocks), encoding="utf-8")
        (LEASES_DIR / f"{d['id']}.pdf").write_bytes(build_pdf(to_lines(blocks)))

        dist = d["dist_to_town_mi"]
        basis = "PLSS-computed" if "plss" in d else "town-approx"
        print(f"{d['id']:32} {basis:14} {d['lat']:9.4f},{d['lon']:9.4f}  "
              f"{(str(dist) + ' mi to ' + d['town']) if dist is not None else '(town point)'}")

        manifest.append({
            "id": d["id"], "markdown": f"leases/{d['id']}.md", "pdf": f"leases/{d['id']}.pdf",
            "doc_type": d["doc_type"], "state": d["state"], "county": d["county"],
            "county_label": d.get("county_label", "County"), "nearest_town": d["town"],
            "latitude": d["lat"], "longitude": d["lon"],
            "coordinate_basis": d["coordinate_basis"],
            "principal_meridian": d["principal_meridian"],
            "distance_to_county_seat_mi": d["dist_to_town_mi"],
            "legal_description_system": d["system"], "legal_description": d["legal"],
            "acres": d.get("acres"),
            "lessor_or_grantor": _first(d, ("lessor", "grantor", "assignor", "farmor",
                                            "applicant", "releasor", "owner", "affiant",
                                            "decedent")),
            "lessee_or_grantee": _first(d, ("lessee", "grantee", "assignee", "farmee",
                                            "operator", "party_b", "executor")),
            "effective_date": d.get("effective_date"), "royalty": d.get("royalty"),
            "bonus": d.get("bonus"), "primary_term": d.get("term"),
        })

    (OUT_DIR / "manifest.json").write_text(
        json.dumps({"documents": manifest}, indent=2) + "\n", encoding="utf-8")
    plss_n = sum(1 for d in DOCS if "plss" in d)
    print(f"\nGenerated {len(DOCS)} documents (.md + .pdf) in {LEASES_DIR}")
    print(f"{plss_n} PLSS tracts geolocated from their legal descriptions; "
          f"{len(DOCS) - plss_n} town-approximate.")
    print(f"Wrote answer key: {OUT_DIR / 'manifest.json'}")


# ---------------------------------------------------------------------------
# Procedural variants: ~4 more documents per template. Data-driven from curated
# real counties + name/amount pools, so the corpus scales without hand-writing
# every dict. PLSS tracts use an inverse solver (plss_from_anchor) so the
# township/range land on the real county seat; non-PLSS use the seat directly.
# ---------------------------------------------------------------------------

def plss_from_anchor(mer: str, lat_t: float, lon_t: float, sec: int, aliquot: str | None) -> dict:
    """Inverse of plss_centroid: pick township/range so the section centroid sits
    on (lat_t, lon_t). Direction (N/S, E/W) follows the sign of the offset."""
    lat0, lon0, _ = MERIDIANS[mer]
    north_total = (lat_t - lat0) * MI_PER_DEG_LAT
    east_total = (lon_t - lon0) * (69.172 * math.cos(math.radians(lat_t)))
    twp_dir = "N" if north_total >= 0 else "S"
    rng_dir = "E" if east_total >= 0 else "W"
    row, col = sec_rowcol(sec)
    ax, ay = parse_aliquot(aliquot)
    needed_north_edge = north_total + (row + 0.5) - (ay - 0.5)
    twp = max(1, round(needed_north_edge / 6)) if twp_dir == "N" \
        else max(1, round(-needed_north_edge / 6) + 1)
    needed_west_edge = east_total - (col + 0.5) - (ax - 0.5)
    rng = max(1, round(needed_west_edge / 6) + 1) if rng_dir == "E" \
        else max(1, round(-needed_west_edge / 6))
    return {"mer": mer, "twp": twp, "twp_dir": twp_dir, "rng": rng, "rng_dir": rng_dir,
            "sec": sec, "aliquot": aliquot}


def pick(pool, i):
    return pool[i % len(pool)]


def short(name: str) -> str:
    return name.split(",")[0]


# Curated real counties: (state, county, label, town, lat, lon, geo). geo selects
# the legal-description system. Coordinates are approximate county-seat points.
def _c(state, county, town, lat, lon, geo, label="County"):
    return {"state": state, "county": county, "label": label, "town": town,
            "lat": lat, "lon": lon, "geo": geo}

PLSS_COUNTIES = [
    _c("New Mexico", "Lea", "Lovington", 32.9445, -103.3486, {"plss": "NMPM"}),
    _c("New Mexico", "Eddy", "Carlsbad", 32.4207, -104.2288, {"plss": "NMPM"}),
    _c("New Mexico", "Chaves", "Roswell", 33.3943, -104.5230, {"plss": "NMPM"}),
    _c("North Dakota", "McKenzie", "Watford City", 47.8022, -103.2832, {"plss": "5th"}),
    _c("North Dakota", "Williams", "Williston", 48.1470, -103.6180, {"plss": "5th"}),
    _c("North Dakota", "Mountrail", "Stanley", 48.3175, -102.3899, {"plss": "5th"}),
    _c("North Dakota", "Dunn", "Killdeer", 47.3722, -102.7521, {"plss": "5th"}),
    _c("Colorado", "Weld", "Greeley", 40.4233, -104.7091, {"plss": "6th"}),
    _c("Colorado", "Garfield", "Glenwood Springs", 39.5505, -107.3248, {"plss": "6th"}),
    _c("Oklahoma", "Kingfisher", "Kingfisher", 35.8620, -97.9320, {"plss": "IM"}),
    _c("Oklahoma", "Canadian", "El Reno", 35.5323, -97.9550, {"plss": "IM"}),
    _c("Oklahoma", "Grady", "Chickasha", 35.0526, -97.9364, {"plss": "IM"}),
    _c("Oklahoma", "Stephens", "Duncan", 34.5023, -97.9578, {"plss": "IM"}),
    _c("Wyoming", "Campbell", "Gillette", 44.2911, -105.5022, {"plss": "6th"}),
    _c("Wyoming", "Converse", "Douglas", 42.7597, -105.3819, {"plss": "6th"}),
    _c("California", "Kern", "Bakersfield", 35.3733, -119.0187, {"plss": "MDM"}),
    _c("Montana", "Carbon", "Red Lodge", 45.1863, -109.2466, {"plss": "MPM"}),
    _c("Montana", "Richland", "Sidney", 47.7169, -104.1564, {"plss": "MPM"}),
    _c("Louisiana", "Caddo", "Shreveport", 32.5252, -93.7502, {"plss": "LA"}, label="Parish"),
]

TX_COUNTIES = [
    _c("Texas", "Midland", "Midland", 31.9973, -102.0779, {"tx": True}),
    _c("Texas", "Reeves", "Pecos", 31.4229, -103.4932, {"tx": True}),
    _c("Texas", "Loving", "Mentone", 31.7060, -103.5977, {"tx": True}),
    _c("Texas", "Reagan", "Big Lake", 31.1932, -101.4663, {"tx": True}),
    _c("Texas", "Karnes", "Karnes City", 28.8853, -97.9003, {"tx": True}),
    _c("Texas", "DeWitt", "Cuero", 29.0938, -97.2886, {"tx": True}),
    _c("Texas", "Howard", "Big Spring", 32.2504, -101.4787, {"tx": True}),
    _c("Texas", "Martin", "Stanton", 32.1290, -101.7888, {"tx": True}),
    _c("Texas", "Ward", "Monahans", 31.5938, -102.8927, {"tx": True}),
    _c("Texas", "Glasscock", "Garden City", 31.8643, -101.4818, {"tx": True}),
    _c("Texas", "La Salle", "Cotulla", 28.4369, -99.2353, {"tx": True}),
    _c("Texas", "Dimmit", "Carrizo Springs", 28.5222, -99.8612, {"tx": True}),
]

METES_COUNTIES = [
    _c("Pennsylvania", "Washington", "Washington", 40.1742, -80.2462, {"metes": True}),
    _c("Pennsylvania", "Greene", "Waynesburg", 39.8962, -80.1811, {"metes": True}),
    _c("Pennsylvania", "Fayette", "Uniontown", 39.8993, -79.7164, {"metes": True}),
    _c("Pennsylvania", "Bradford", "Towanda", 41.7670, -76.4438, {"metes": True}),
    _c("Pennsylvania", "Susquehanna", "Montrose", 41.8362, -75.8788, {"metes": True}),
    _c("West Virginia", "Doddridge", "West Union", 39.2940, -80.7762, {"metes": True}),
    _c("West Virginia", "Harrison", "Clarksburg", 39.2806, -80.3445, {"metes": True}),
    _c("West Virginia", "Wetzel", "New Martinsville", 39.6453, -80.8576, {"metes": True}),
    _c("West Virginia", "Ritchie", "Harrisville", 39.2087, -81.0518, {"metes": True}),
    _c("Ohio", "Belmont", "St. Clairsville", 40.0801, -80.9009, {"metes": True}),
    _c("Ohio", "Monroe", "Woodsfield", 39.7617, -81.1170, {"metes": True}),
    _c("Ohio", "Harrison", "Cadiz", 40.2723, -81.0120, {"metes": True}),
    _c("Ohio", "Guernsey", "Cambridge", 40.0312, -81.5885, {"metes": True}),
]

GRANT_COUNTIES = [
    _c("New Mexico", "Rio Arriba", "Tierra Amarilla", 36.7045, -106.5464, {"grant": True}),
]

MIXED_COUNTIES = PLSS_COUNTIES + TX_COUNTIES + METES_COUNTIES

LESSORS = [
    "Margaret A. Caldwell, a single woman", "The Holloway Family Trust dated June 3, 1998",
    "James R. and Linda S. Whitaker, husband and wife", "Circle Bar Ranch, LLC",
    "Estate of Harlan W. Dietrich, by Susan Dietrich, Personal Representative",
    "Clayton and Beatrice Monroe, husband and wife", "The Reaves Living Trust",
    "Samuel O. Pruett, a married man dealing in his sole and separate property",
    "Nadine F. Holloway, a widow", "Sage Creek Minerals, LLC",
    "Bigelow Family Partnership, LP", "Doris and Walter Kestrel, as joint tenants",
]
GRANTORS = [
    "Dorothy M. Albright, a widow", "Roaring Fork Holdings, LLC", "Ramon C. Trujillo",
    "University Lands Heritage Trust", "Esperanza Ranch Partners, Ltd.",
    "Cuero Creek Ranch, Ltd.", "Beaulieu Land & Timber, L.L.C.", "Pecos Valley Land Company",
]
GRANTEES = [
    "Chisholm Trail Royalties, LLC", "Santa Rita Royalty Company", "The Trujillo Family, LLC",
    "Permian Acquisition Partners, LP", "Gulf Coast Minerals, LLC", "Caleb and Marie Donnelly, as joint tenants",
]
OPERATORS = [
    "Llano Estacado Operating, LLC", "Delaware Basin Resources, LP", "Mesa Verde Resources, LP",
    "Bakken Ridge Energy, Inc.", "Front Range Petroleum, LLC", "Red River Minerals, LLC",
    "Keystone Shale Partners, LP", "Allegheny Gas Company", "Buckeye Utica Operating, LLC",
    "Caddo Pine Energy, LLC", "Powder River Resources, LLC", "San Joaquin Oil Company",
    "Permian Basin Production Co.", "Cimarron Operating, LLC", "Lone Mesa Exploration, Inc.",
]
ROYALTIES = ["one-eighth (1/8)", "three-sixteenths (3/16)", "one-fifth (1/5)", "one-fourth (1/4)",
             "18.75% (3/16)", "20% (1/5)", "22.5% (9/40)", "one-sixth (1/6)"]
BONUSES = ["$500.00 per net mineral acre", "$1,000.00 per net mineral acre",
           "$1,500.00 per net mineral acre", "$2,000.00 per net acre",
           "$2,750.00 per net acre", "$350.00 per net mineral acre", "$3,000.00 per net acre"]
TERMS = ["three (3) years", "five (5) years", "two (2) years",
         "three (3) years with a two-year option"]
DATES = ["January 12, 2024", "March 4, 2024", "June 21, 2024", "August 30, 2024",
         "October 17, 2024", "December 5, 2024", "February 14, 2025", "April 9, 2025",
         "May 27, 2025", "July 3, 2025", "September 15, 2025"]
SHUT_IN = ["$25.00", "$50.00", "$100.00"]
CONSIDERATIONS = ["$10.00 and other good and valuable consideration", "$48,000.00",
                  "$120,000.00", "$385,000.00", "Ten Dollars and other valuable consideration"]
FORMS = ["88-PB", "TX-88", "OK-88", "NM-PB", "CA-88", "PR-88"]
FORMATIONS = ["Wolfcamp A", "Bone Spring", "Spraberry", "Bakken and Three Forks", "Niobrara",
              "Mississippian", "Marcellus", "Utica", "Eagle Ford", "Wolfcamp B"]
DEPTHS = ["8,500", "10,500", "11,200", "12,000", "9,800", "10,600 TVD / 21,000 MD"]
SPUDS = ["March 2024", "July 2024", "Q4 2024", "January 2025", "Spring 2025"]
SECTIONS = [4, 9, 14, 16, 21, 22, 27, 33]
ALIQUOTS = ["SE/4", "N/2", "NW/4", "SW/4", "NE/4", "S/2", "W/2", "all"]
ALIQUOT_ACRES = {"SE/4": "160.00", "NE/4": "160.00", "NW/4": "160.00", "SW/4": "160.00",
                 "N/2": "320.00", "S/2": "320.00", "E/2": "320.00", "W/2": "320.00",
                 "all": "640.00"}
NPACRES = ["40.00", "80.00", "160.00", "120.50", "210.50", "320.00", "640.00"]
SURVEYS = ["T&P RR Co.", "H&GN RR Co.", "H&TC RR Co.", "GC&SF Ry Co.", "I&GN RR Co.", "PSL", "BS&F"]
TXSEC = [7, 14, 22, 30, 3, 18, 40]
TXBLK = ["34", "13", "2", "C-24", "X", "41", "39"]
ABST = [89, 612, 1187, 2204, 456, 36, 1450]
NEIGHBORS = ["Reynolds", "Maple Run", "Hartzell", "Coen", "Yoder", "Buchanan"]
DEEDBOOKS = ["Deed Book 1123, Page 456", "Instrument No. 2016-004412", "Deed Book 244, Page 19",
             "Deed Book 512, Page 88"]
GRANTS = ["Tierra Amarilla", "Sangre de Cristo", "Mora"]
PA_DISTRICTS = ["Amwell Township", "Morris Township", "Center Township", "Franklin Township"]
WV_DISTRICTS = ["McClellan District", "Grant District", "Union District", "Clay District"]
OH_DISTRICTS = ["Mead Township", "Smith Township", "Washington Township", "Wayne Township"]
EXAMINERS = ["T. Lindqvist, Attorney at Law, Lindqvist & Reyes PLLC",
             "M. Castellano, Castellano Title Law PC", "R. Whitfield, Whitfield & Boyd LLP",
             "S. Nakamura, Nakamura Energy Law"]
ABSTRACTORS = ["Trans-Pecos Abstract Co.", "Permian Abstract & Title", "High Plains Land Services",
               "Frontier Abstract Company"]
WELL_SURNAMES = ["Caldwell", "Holloway", "Whitaker", "Bergstrom", "Monroe", "Reaves", "Pruett",
                 "Kestrel", "Bigelow", "Stanton", "Carpenter", "Dietrich"]
INTERESTS = ["one-half (1/2)", "one-fourth (1/4)", "an undivided 1/8", "a 1/32 of 8/8"]
COMMISSIONS = {"Oklahoma": "Oklahoma Corporation Commission",
               "North Dakota": "North Dakota Industrial Commission",
               "Wyoming": "Wyoming Oil and Gas Conservation Commission",
               "New Mexico": "New Mexico Oil Conservation Division",
               "Colorado": "Colorado Energy and Carbon Management Commission",
               "Montana": "Montana Board of Oil and Gas Conservation",
               "Louisiana": "Louisiana Office of Conservation",
               "California": "California Geologic Energy Management Division"}
STATE_ABBR = {"New Mexico": "nm", "North Dakota": "nd", "Colorado": "co", "Oklahoma": "ok",
              "Wyoming": "wy", "California": "ca", "Montana": "mt", "Louisiana": "la",
              "Texas": "tx", "Pennsylvania": "pa", "West Virginia": "wv", "Ohio": "oh"}


def well_name(n):
    return f"{pick(WELL_SURNAMES, n)} {pick(SECTIONS, n)}-{10 + n % 40} #{1 + n % 3}H"


def _rec(n):
    yr = pick(["2022", "2023", "2024", "2025"], n)
    return {"instrument_no": f"{yr}-{(1000 + n * 53) % 9999999:07d}",
            "book": str(700 + n * 7), "page": str(15 + n * 11)}


def tx_legal(n):
    return f"Section {pick(TXSEC, n)}, Block {pick(TXBLK, n)}, {pick(SURVEYS, n)} Survey, Abstract No. {pick(ABST, n)}"


def metes_legal(n):
    return (f"BEGINNING at an iron pin at a corner of lands now or formerly of {pick(NEIGHBORS, n)}; "
            f"thence S {70 + n % 15} deg E {900 + n * 37} feet to a post; thence S {3 + n % 9} deg W "
            f"{2600 + n * 23} feet to a stone; thence N {72 + n % 14} deg W {950 + n * 31} feet to a "
            f"marked oak; thence N {2 + n % 8} deg E {2580 + n * 19} feet to the place of beginning")


def grant_legal(n):
    return (f"A portion of the {pick(GRANTS, n)} Land Grant, Tract {pick(['7-B', '3-A', '12', '5-C'], n)}, "
            f"as shown on the amended grant plat of record")


def apply_location(doc, county, sec, aliquot, n):
    doc["state"], doc["county"], doc["town"] = county["state"], county["county"], county["town"]
    if county.get("label", "County") != "County":
        doc["county_label"] = county["label"]
    geo = county["geo"]
    if "plss" in geo:
        doc["town_lat"], doc["town_lon"] = county["lat"], county["lon"]
        doc["plss"] = plss_from_anchor(geo["plss"], county["lat"], county["lon"], sec, aliquot)
        doc["acres"] = ALIQUOT_ACRES.get(aliquot, "160.00")
    else:
        doc["lat"], doc["lon"] = county["lat"], county["lon"]
        doc["acres"] = pick(NPACRES, n)
        if "tx" in geo:
            doc["system"], doc["legal"] = "Texas abstract/block-section", tx_legal(n)
        elif "metes" in geo:
            doc["system"] = "metes and bounds"
            dpool = {"Pennsylvania": PA_DISTRICTS, "West Virginia": WV_DISTRICTS,
                     "Ohio": OH_DISTRICTS}[county["state"]]
            doc["township"] = pick(dpool, n)
            doc["legal"] = metes_legal(n)
        elif "grant" in geo:
            doc["system"], doc["legal"] = "Spanish/Mexican land grant (tract)", grant_legal(n)
            doc["acres"] = pick(["35.75", "52.40", "18.90"], n)


def _afe_costs(n):
    f = pick([0.8, 1.0, 1.25], n)
    rows = [("Location, roads, pad", 250000, 0), ("Drilling rig & tools", 3200000, 0),
            ("Drilling fluids & chemicals", 680000, 0), ("Cementing", 420000, 150000),
            ("Logging, testing, supervision", 510000, 240000), ("Surface & intermediate casing", 640000, 0),
            ("Production casing & tubing", 0, 1250000), ("Wellhead & tree", 0, 180000),
            ("Hydraulic fracturing", 0, 4800000), ("Facilities & tank battery", 0, 520000)]
    dry = sum(int(r[1] * f) for r in rows)
    comp = sum(int(r[2] * f) for r in rows)
    out = [["Cost Item", "Dry Hole ($)", "Completion ($)"]]
    out += [[name, f"{int(d * f):,}", f"{int(c * f):,}"] for name, d, c in rows]
    out.append(["Contingency (10%)", f"{int(dry * 0.1):,}", f"{int(comp * 0.1):,}"])
    out.append(["TOTAL ESTIMATE", f"{int(dry * 1.1):,}", f"{int(comp * 1.1):,}"])
    return out


# Per-template field factories (location applied separately). Keyed by template name.
EXTRA = {
    "oil_gas_lease": lambda n: {"form_no": pick(FORMS, n), "lessor": pick(LESSORS, n),
        "lessee": pick(OPERATORS, n + 3), "effective_date": pick(DATES, n), "term": pick(TERMS, n),
        "royalty": pick(ROYALTIES, n), "bonus": pick(BONUSES, n), "shut_in": pick(SHUT_IN, n)},
    "paidup_modern": lambda n: {"lessor": pick(LESSORS, n + 1), "lessee": pick(OPERATORS, n),
        "effective_date": pick(DATES, n + 2), "term": pick(TERMS, n + 1), "royalty": pick(ROYALTIES, n + 1),
        "bonus": pick(BONUSES, n + 1)},
    "metes_bounds_lease": lambda n: {"lessor": pick(LESSORS, n + 2), "lessee": pick(OPERATORS, n + 1),
        "effective_date": pick(DATES, n + 3), "term": pick(TERMS, n), "royalty": pick(ROYALTIES, n),
        "oil_royalty": "one-eighth (1/8)", "bonus": pick(BONUSES, n), "source_deed": pick(DEEDBOOKS, n)},
    "memorandum": lambda n: {"lessor": pick(LESSORS, n), "lessee": pick(OPERATORS, n),
        "effective_date": pick(DATES, n), "term": pick(TERMS, n), "royalty": pick(ROYALTIES, n),
        "recording": _rec(n)},
    "mineral_deed": lambda n: {"grantor": pick(GRANTORS, n), "grantee": pick(GRANTEES, n),
        "consideration": pick(CONSIDERATIONS, n), "interest": pick(INTERESTS, n),
        "warranty": pick(["general", "special"], n), "effective_date": pick(DATES, n)},
    "royalty_deed": lambda n: {"grantor": pick(GRANTORS, n + 1), "grantee": pick(GRANTEES, n + 1),
        "consideration": pick(CONSIDERATIONS, n + 1), "interest": pick(INTERESTS, n + 2),
        "effective_date": pick(DATES, n + 1)},
    "warranty_deed": lambda n: {"grantor": pick(GRANTORS, n + 2), "grantee": pick(GRANTEES, n + 2),
        "consideration": pick(CONSIDERATIONS, n + 2), "reservation": pick(["one-half (1/2)", "one-fourth (1/4)"], n),
        "effective_date": pick(DATES, n + 2)},
    "quitclaim": lambda n: {"grantor": pick(GRANTORS, n + 3), "grantee": pick(GRANTEES, n + 3),
        "consideration": pick(CONSIDERATIONS, n + 3), "effective_date": pick(DATES, n + 3)},
    "surface_use": lambda n: {"lessor": pick(LESSORS, n), "lessee": pick(OPERATORS, n),
        "effective_date": pick(DATES, n), "surface_payment": pick(["$20,000.00", "$25,000.00", "$30,000.00"], n),
        "road_payment": pick(["$25.00", "$30.00", "$40.00"], n)},
    "easement": lambda n: {"grantor": pick(GRANTORS, n), "grantee": pick(["Guadalupe Midstream Partners, LP",
        "Permian Gathering Co.", "Llano Pipeline LLC"], n), "consideration": pick(["$45,000.00", "$60,300.00",
        "$72,000.00"], n), "perm_width": "30", "temp_width": "50", "rods": str(300 + n * 17),
        "effective_date": pick(DATES, n), "acres": pick(["3.50", "4.20", "5.75"], n)},
    "title_opinion": lambda n: {"lessee": pick(OPERATORS, n), "examiner": pick(EXAMINERS, n),
        "abstractor": pick(ABSTRACTORS, n), "abstract_entries": str(40 + n * 3),
        "cert_date": pick(DATES, n), "mineral_owner": short(pick(LESSORS, n)),
        "npri": "1/8 of 8/8", "npri_owner": "the Henderson Family", "lease_royalty": "25% (1/4)",
        "nri": "0.65625", "effective_date": pick(DATES, n + 1)},
    "grazing_lease": lambda n: {"lessor": pick(LESSORS, n), "lessee": pick(["Rock Creek Cattle Company",
        "Beartooth Land & Livestock", "Sweetwater Grazing Co."], n), "effective_date": pick(DATES, n),
        "term": pick(TERMS, n), "rent": pick(["$12.00 per acre", "$18.00 per acre", "$22.00 per acre"], n),
        "rent_schedule": "annually in advance", "aum": str(150 + n * 30)},
    "amendment": lambda n: {"lessor": pick(LESSORS, n), "lessee": pick(OPERATORS, n),
        "orig_date": pick(DATES, n), "effective_date": pick(DATES, n + 4), "term": pick(TERMS, n),
        "royalty": pick(ROYALTIES, n + 1), "bonus": pick(BONUSES, n), "recording": _rec(n)},
    "doto": lambda n: {"operator": pick(OPERATORS, n), "examiner": pick(EXAMINERS, n),
        "well": well_name(n), "drilling_opinion_date": pick(DATES, n), "effective_date": pick(DATES, n + 5),
        "division": [["Owner", "Interest Type", "Decimal"], [short(pick(LESSORS, n)), "Royalty (RI)", "0.18750000"],
                     ["Big Bend Royalty Partners", "ORRI", "0.03125000"],
                     [pick(OPERATORS, n), "Working Interest (NRI)", "0.78125000"]]},
    "division_order": lambda n: {"operator": pick(OPERATORS, n), "property_no": f"DO-{1000 + n}",
        "well": well_name(n), "effective_date": pick(DATES, n),
        "owners": [["Owner No.", "Owner Name", "Type", "Decimal Interest"],
                   ["0001", short(pick(LESSORS, n)), "RI", "0.18750000"],
                   ["0100", pick(OPERATORS, n), "WI", "0.81250000"]]},
    "affidavit_heirship": lambda n: {"affiant": f"{pick(NEIGHBORS, n)} {pick(['K. Hayes', 'L. Boyd', 'R. Tao'], n)}, a disinterested party",
        "decedent": f"{pick(['Andrew J.', 'Walter P.', 'Esther M.', 'Roy D.'], n)} {pick(WELL_SURNAMES, n)}",
        "date_of_death": pick(DATES, n), "place_of_death": pick(["Pecos, Texas", "Roswell, New Mexico",
        "Gillette, Wyoming"], n), "testacy": pick(["leaving a written will", "intestate (without a will)"], n),
        "spouse": f"Helen {pick(WELL_SURNAMES, n)}", "spouse_status": "predeceased the Decedent",
        "effective_date": pick(DATES, n + 1),
        "heirs": [["Name", "Relationship", "Share of Decedent's Interest"],
                  [f"Carl {pick(WELL_SURNAMES, n)}", "Son", "1/2"],
                  [f"Diane {pick(WELL_SURNAMES, n)}", "Daughter", "1/2"]]},
    "probate_order": lambda n: {"court": pick(["COUNTY COURT", "DISTRICT COURT", "PROBATE COURT"], n),
        "decedent": f"{pick(['Andrew J.', 'Walter P.', 'Esther M.', 'Roy D.'], n)} {pick(WELL_SURNAMES, n)}",
        "cause_no": f"P-{1000 + n}", "date_of_death": pick(DATES, n), "will_date": pick(DATES, n + 2),
        "executor": f"Carl {pick(WELL_SURNAMES, n)}", "effective_date": pick(DATES, n + 3)},
    "assignment_absc": lambda n: {"assignor": pick(OPERATORS, n), "assignee": pick(["Permian Acquisition Partners, LP",
        "Basin A&D Holdings, LLC", "Frontier Upstream, LP"], n), "consideration": pick(CONSIDERATIONS, n),
        "orri": pick(["2.0%", "2.5%", "3.0%"], n), "effective_date": pick(DATES, n),
        "leases": [["Lease (Lessor)", "County", "Legal Description", "Recording"],
                   [short(pick(LESSORS, n)), "see caption", "primary tract (this instrument)", f"Vol {700 + n}/Pg {20 + n}"],
                   [short(pick(LESSORS, n + 1)), "adjoining", "secondary tract per Exhibit A", f"Vol {705 + n}/Pg {41 + n}"]]},
    "joa": lambda n: {"operator": pick(OPERATORS, n), "effective_date": pick(DATES, n),
        "formation": pick(FORMATIONS, n), "nonconsent": pick(["300%", "400%"], n),
        "afe_threshold": pick(["$50,000.00", "$100,000.00"], n),
        "interests": [["Party", "Working Interest"], [f"{pick(OPERATORS, n)} (Operator)", "65.00%"],
                      [pick(OPERATORS, n + 1), "20.00%"], [pick(OPERATORS, n + 2), "15.00%"]]},
    "farmout": lambda n: {"farmor": pick(GRANTORS, n), "farmee": pick(OPERATORS, n),
        "commence_by": pick(DATES, n + 4), "formation": pick(FORMATIONS, n), "depth": pick(DEPTHS, n),
        "earned_depths": f"from the surface to the base of the {pick(FORMATIONS, n)}",
        "orri": pick(["3.0%", "3.5%", "4.0%"], n), "backin": "25%", "effective_date": pick(DATES, n)},
    "ami": lambda n: {"party_a": pick(OPERATORS, n), "party_b": pick(OPERATORS, n + 4),
        "proportions": pick(["50% / 50%", "60% / 40%", "75% / 25%"], n),
        "ami_area": "the captioned lands and all surveys/sections adjoining them",
        "term": pick(["two (2) years", "three (3) years", "five (5) years"], n),
        "effective_date": pick(DATES, n)},
    "pooling_order": lambda n: {"applicant": pick(OPERATORS, n), "operator": pick(OPERATORS, n),
        "formation": pick(FORMATIONS, n), "cause_no": f"CD 2024-{100000 + n}", "order_no": str(700000 + n),
        "effective_date": pick(DATES, n),
        "options": [["Election Option", "Cash Bonus / Net Acre", "Royalty"],
                    ["(a) Participate (share of est. well cost $3,400,000)", "n/a", "n/a"],
                    ["(b) Cash bonus plus royalty", pick(["$500", "$1,000", "$1,500"], n), "3/16"],
                    ["(c) Higher royalty, lower bonus", "$200", "1/5"]]},
    "release_lease": lambda n: {"releasor": pick(OPERATORS, n), "orig_lessor": short(pick(LESSORS, n)),
        "orig_date": pick(DATES, n), "effective_date": pick(DATES, n + 5), "orig_recording": _rec(n)},
    "ratification": lambda n: {"owner": f"{short(pick(LESSORS, n))} Mineral Trust",
        "orig_date": pick(DATES, n), "royalty": pick(ROYALTIES, n), "effective_date": pick(DATES, n + 2),
        "orig_recording": _rec(n)},
    "afe": lambda n: {"operator": pick(OPERATORS, n), "afe_no": f"AFE-2024-{500 + n}",
        "well": well_name(n), "depth": pick(DEPTHS, n), "formation": pick(FORMATIONS, n),
        "spud": pick(SPUDS, n), "effective_date": pick(DATES, n), "costs": _afe_costs(n)},
}

DOC_TYPE = {
    "oil_gas_lease": "Oil and Gas Lease", "paidup_modern": "Oil and Gas Lease",
    "metes_bounds_lease": "Oil and Gas Lease", "memorandum": "Memorandum of Oil and Gas Lease",
    "mineral_deed": "Mineral Deed", "royalty_deed": "Royalty Deed",
    "warranty_deed": "General Warranty Deed", "quitclaim": "Quitclaim Deed",
    "surface_use": "Surface Use and Damage Agreement", "easement": "Right-of-Way and Pipeline Easement",
    "title_opinion": "Drilling Title Opinion", "grazing_lease": "Grazing and Ranch Lease",
    "amendment": "Lease Amendment and Extension", "doto": "Division Order Title Opinion",
    "division_order": "Division Order", "affidavit_heirship": "Affidavit of Heirship",
    "probate_order": "Order Admitting Will to Probate",
    "assignment_absc": "Assignment, Bill of Sale and Conveyance", "joa": "Joint Operating Agreement",
    "farmout": "Farmout Agreement", "ami": "Area of Mutual Interest Agreement",
    "pooling_order": "Pooling Order", "release_lease": "Release of Oil and Gas Lease",
    "ratification": "Ratification of Oil and Gas Lease", "afe": "Authority for Expenditure (AFE)",
}
SLUG = {
    "oil_gas_lease": "ogl", "paidup_modern": "ogl-pu", "metes_bounds_lease": "ogl-mb",
    "memorandum": "memo", "mineral_deed": "mineral-deed", "royalty_deed": "royalty-deed",
    "warranty_deed": "warranty-deed", "quitclaim": "quitclaim", "surface_use": "surface-use",
    "easement": "easement", "title_opinion": "title-opinion", "grazing_lease": "grazing",
    "amendment": "amendment", "doto": "doto", "division_order": "division-order",
    "affidavit_heirship": "affidavit", "probate_order": "probate", "assignment_absc": "absc",
    "joa": "joa", "farmout": "farmout", "ami": "ami", "pooling_order": "pooling",
    "release_lease": "release", "ratification": "ratification", "afe": "afe",
}
# Which county pool each template draws from (legal-description system must fit).
POOLS = {
    "metes_bounds_lease": METES_COUNTIES,
    "pooling_order": PLSS_COUNTIES, "doto": PLSS_COUNTIES, "division_order": PLSS_COUNTIES,
    "joa": PLSS_COUNTIES, "afe": PLSS_COUNTIES, "farmout": PLSS_COUNTIES, "ratification": PLSS_COUNTIES,
    "quitclaim": GRANT_COUNTIES + TX_COUNTIES,
}
EXTRA_PER_TEMPLATE = 4


def build_extra_docs():
    docs = []
    n = 37
    for template in EXTRA:
        pool = POOLS.get(template, MIXED_COUNTIES)
        for _ in range(EXTRA_PER_TEMPLATE):
            county = pool[n % len(pool)]
            sec, aliquot = pick(SECTIONS, n), pick(ALIQUOTS, n)
            doc = {"id": f"{n:03d}-{SLUG[template]}-{county['county'].lower().replace(' ', '')}-{STATE_ABBR[county['state']]}",
                   "template": template, "doc_type": DOC_TYPE[template]}
            apply_location(doc, county, sec, aliquot, n)
            doc.update(EXTRA[template](n))
            if template == "pooling_order":
                doc["commission"] = COMMISSIONS.get(county["state"], "State Oil and Gas Commission")
            docs.append(doc)
            n += 1
    return docs


DOCS += build_extra_docs()


if __name__ == "__main__":
    render_all()
