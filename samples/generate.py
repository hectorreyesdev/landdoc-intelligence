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

Everything here is SYNTHETIC. Party names are invented. Counties, parishes,
meridians, and approximate coordinates are real US places so the documents map
to actual locations (for a future map feature) and exercise the real legal-
description systems (PLSS section-township-range, Texas abstract/block-section,
and Appalachian metes-and-bounds).
"""
from __future__ import annotations

import json
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

    # self-check: every xref offset must land on "<id> 0 obj"
    for oid in range(1, total + 1):
        assert out[offsets[oid]:].startswith(f"{oid} 0 obj".encode()), f"bad offset for obj {oid}"
    return bytes(out)


# ---------------------------------------------------------------------------
# Block model: documents are authored as (kind, text) blocks, then rendered to
# both Markdown and wrapped plain text (for the PDF) from the same source.
# kind in {"h1", "h2", "p", "blank"}
# ---------------------------------------------------------------------------

def to_markdown(blocks: list[tuple[str, str]]) -> str:
    md: list[str] = []
    for kind, text in blocks:
        if kind == "h1":
            md.append(f"# {text}\n")
        elif kind == "h2":
            md.append(f"## {text}\n")
        elif kind == "blank":
            md.append("")
        else:
            md.append(text + "\n")
    return "\n".join(md).strip() + "\n"


def to_lines(blocks: list[tuple[str, str]]) -> list[str]:
    lines: list[str] = []
    for kind, text in blocks:
        if kind == "blank":
            lines.append("")
        elif kind in ("h1", "h2"):
            lines.append(text.upper() if kind == "h1" else text)
            lines.append("")
        else:
            lines.extend(textwrap.wrap(text, WRAP_COLS) or [""])
    return lines


# ---------------------------------------------------------------------------
# Reusable clause fragments
# ---------------------------------------------------------------------------

def _notary(state: str, county_label: str, county: str, who: str) -> list[tuple[str, str]]:
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


def _recording_block(rec: dict) -> list[tuple[str, str]]:
    return [
        ("p", "[RECORDING DATA]"),
        ("p", f"Instrument No.: {rec['instrument_no']}"),
        ("p", f"Book/Volume: {rec['book']}   Page: {rec['page']}"),
        ("p", f"Recorded: {rec['recorded']}   {rec['recorder']}"),
    ]


# ---------------------------------------------------------------------------
# Templates (one per instrument style)
# ---------------------------------------------------------------------------

def t_oil_gas_lease(d) -> list[tuple[str, str]]:
    county_label = d.get("county_label", "County")
    b = [
        ("h1", "Oil and Gas Lease"),
        ("p", f"(Producers 88 -- Paid-Up) Form No. {d['form_no']}"),
        ("blank", ""),
        ("p", f"THIS AGREEMENT made this {d['effective_date']}, by and between "
              f"{d['lessor']} (\"Lessor\", whether one or more), and "
              f"{d['lessee']} (\"Lessee\")."),
        ("blank", ""),
        ("h2", "1. Granting Clause"),
        ("p", f"Lessor, in consideration of {d['bonus']} and the covenants herein, grants, leases, and "
              f"lets exclusively unto Lessee the land described below for the purpose of exploring for, "
              f"drilling, and producing oil and gas, together with rights of ingress and egress."),
        ("h2", "2. Description of Leased Premises"),
        ("p", f"The leased premises are situated in {d['county']} {county_label}, {d['state']}, and "
              f"described as: {d['legal']}, containing {d['acres']} acres, more or less "
              f"(the \"Leased Premises\")."),
        ("h2", "3. Primary Term"),
        ("p", f"This lease shall remain in force for a primary term of {d['term']} from the effective "
              f"date (the \"Primary Term\") and as long thereafter as oil or gas is produced in paying "
              f"quantities from the Leased Premises or lands pooled therewith."),
        ("h2", "4. Royalty"),
        ("p", f"Lessee shall pay Lessor a royalty of {d['royalty']} of the gross proceeds of all oil "
              f"and gas produced and sold from the Leased Premises, free of costs of production but "
              f"bearing its proportionate share of post-production costs as permitted by law."),
        ("h2", "5. Shut-In Royalty"),
        ("p", f"If a well capable of producing gas is shut in, Lessee may maintain this lease by paying "
              f"a shut-in royalty of {d['shut_in']} per net mineral acre per year."),
        ("h2", "6. Pooling"),
        ("p", "Lessee may pool the Leased Premises with other lands to form a unit not exceeding 640 "
              "acres (plus 10% tolerance) for an oil well or 1,280 acres for a horizontal gas well, in "
              "conformity with applicable spacing rules."),
        ("h2", "7. Warranty and Surrender"),
        ("p", "Lessor warrants title to the Leased Premises. Lessee may surrender this lease, in whole "
              "or in part, by recording a release. This lease binds the heirs, successors, and assigns "
              "of the parties."),
        ("blank", ""),
        ("p", "IN WITNESS WHEREOF, the parties execute this lease as of the effective date."),
        ("blank", ""),
        ("p", f"LESSOR: {d['lessor']}"),
        ("p", f"LESSEE: {d['lessee']}"),
    ]
    b += _notary(d["state"], county_label, d["county"], d["lessor"])
    return b


def t_paidup_modern(d) -> list[tuple[str, str]]:
    return [
        ("h1", "Paid-Up Oil and Gas Lease"),
        ("p", f"Effective Date: {d['effective_date']}"),
        ("p", f"Lessor: {d['lessor']}"),
        ("p", f"Lessee: {d['lessee']}"),
        ("p", f"County: {d['county']}, {d['state']}"),
        ("blank", ""),
        ("h2", "Recitals"),
        ("p", f"Lessor owns an interest in the oil, gas, and other minerals underlying the lands "
              f"described herein and desires to lease the same to Lessee for development."),
        ("h2", "Leased Lands"),
        ("p", f"{d['legal']}, containing approximately {d['acres']} net mineral acres in "
              f"{d['county']} County, {d['state']}."),
        ("h2", "Consideration and Bonus"),
        ("p", f"Lessee has paid Lessor {d['bonus']} as a paid-up bonus, the receipt of which is "
              f"acknowledged, covering the full primary term with no delay rentals due."),
        ("h2", "Term"),
        ("p", f"Primary term of {d['term']}, and so long thereafter as operations or production "
              f"continue on the leased lands or lands pooled therewith."),
        ("h2", "Royalty"),
        ("p", f"{d['royalty']} of production, delivered or paid free of the costs of exploration, "
              f"drilling, and production."),
        ("h2", "Depth and Continuous Operations"),
        ("p", "This lease covers all depths. A continuous-development clause requires Lessee to "
              "commence a new well within 180 days of completion of the prior well to hold acreage "
              "outside a producing unit."),
        ("h2", "Execution"),
        ("p", f"Executed by {d['lessor']} (Lessor) and {d['lessee']} (Lessee) effective "
              f"{d['effective_date']}."),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_metes_bounds_lease(d) -> list[tuple[str, str]]:
    return [
        ("h1", "Oil and Gas Lease (Appalachian Form)"),
        ("p", f"This Lease, made and entered into on {d['effective_date']}, between {d['lessor']}, of "
              f"{d['township']}, {d['county']} County, {d['state']} (\"Lessor\"), and "
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
        ("h2", "Delay in Marketing / Free Gas"),
        ("p", "Lessor shall have the right to use up to 200,000 cubic feet of gas per year, free of "
              "charge, for one dwelling on the premises, at Lessor's own risk."),
        ("blank", ""),
        ("p", f"WITNESS the hand and seal of {d['lessor']}, Lessor."),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_memorandum(d) -> list[tuple[str, str]]:
    blocks = [
        ("h1", "Memorandum of Oil and Gas Lease"),
        ("p", "(Short Form for Recording)"),
        ("blank", ""),
    ]
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
    blocks += _notary(d["state"], "County", d["county"], d["lessor"])
    return blocks


def t_mineral_deed(d) -> list[tuple[str, str]]:
    return [
        ("h1", "Mineral Deed"),
        ("p", f"KNOW ALL PERSONS BY THESE PRESENTS, that {d['grantor']} (\"Grantor\"), for and in "
              f"consideration of {d['consideration']}, the receipt of which is acknowledged, does "
              f"hereby GRANT, SELL, and CONVEY unto {d['grantee']} (\"Grantee\") the following:"),
        ("h2", "Mineral Interest Conveyed"),
        ("p", f"An undivided {d['interest']} interest in and to all of the oil, gas, and other "
              f"minerals in and under, and that may be produced from, the following described land:"),
        ("h2", "Land"),
        ("p", f"{d['legal']}, containing {d['acres']} acres, more or less, situated in {d['county']} "
              f"County, {d['state']}."),
        ("h2", "Habendum"),
        ("p", "TO HAVE AND TO HOLD the above-described mineral interest, together with all rights "
              "of ingress and egress, unto Grantee, Grantee's heirs, successors, and assigns "
              "forever. Grantor binds Grantor's heirs to warrant and forever defend title."),
        ("p", f"This conveyance is made subject to any valid and subsisting oil and gas lease of "
              f"record, but covers and includes {d['interest']} of all rentals and royalties "
              f"thereunder."),
        ("blank", ""),
        ("p", f"EXECUTED this {d['effective_date']}."),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_royalty_deed(d) -> list[tuple[str, str]]:
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
              "right to execute leases, to receive bonus or delay rentals, or to participate in the "
              "making of oil and gas leases, all such rights being reserved to the mineral owner."),
        ("blank", ""),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_warranty_deed(d) -> list[tuple[str, str]]:
    return [
        ("h1", "General Warranty Deed"),
        ("p", f"THE STATE OF {d['state'].upper()}"),
        ("p", f"COUNTY OF {d['county'].upper()}"),
        ("blank", ""),
        ("p", f"That {d['grantor']} (\"Grantor\"), for and in consideration of the sum of "
              f"{d['consideration']} cash in hand paid by {d['grantee']} (\"Grantee\"), the receipt "
              f"and sufficiency of which are acknowledged, has GRANTED, SOLD, and CONVEYED, and by "
              f"these presents does GRANT, SELL, and CONVEY unto Grantee the following real property:"),
        ("h2", "Property"),
        ("p", f"{d['legal']}, containing {d['acres']} acres, more or less, situated in {d['county']} "
              f"County, {d['state']}, together with all improvements thereon (the \"Property\")."),
        ("h2", "Reservations and Exceptions"),
        ("p", f"Grantor RESERVES unto Grantor, Grantor's heirs and assigns, an undivided "
              f"{d['reservation']} of all oil, gas, and other minerals in, on, and under the "
              f"Property. This conveyance is subject to all easements, restrictions, and mineral "
              f"reservations of record."),
        ("h2", "Habendum and Warranty"),
        ("p", "TO HAVE AND TO HOLD the Property unto Grantee, Grantee's heirs, successors, and "
              "assigns forever; and Grantor binds Grantor and Grantor's heirs to WARRANT AND FOREVER "
              "DEFEND all and singular the Property unto Grantee against every person lawfully "
              "claiming the same."),
        ("blank", ""),
        ("p", f"EXECUTED on {d['effective_date']}."),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_quitclaim(d) -> list[tuple[str, str]]:
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


def t_surface_use(d) -> list[tuple[str, str]]:
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
        ("h2", "Surface Damages"),
        ("p", f"Operator shall pay Surface Owner {d['surface_payment']} per well pad as initial "
              f"surface damages, plus {d['road_payment']} per rod for new roads and pipelines "
              f"installed across the property."),
        ("h2", "Location of Operations"),
        ("p", "Well pads, tank batteries, and central facilities shall be located no closer than "
              "500 feet to any existing residence or water well without the Surface Owner's written "
              "consent. Operator shall fence all production facilities and control noxious weeds."),
        ("h2", "Reclamation"),
        ("p", "Upon plugging and abandonment, Operator shall remove all equipment, recontour the "
              "site to approximate original grade, replace topsoil, and reseed with a mix approved "
              "by Surface Owner within twelve (12) months."),
        ("h2", "Water"),
        ("p", "Operator shall not use fresh water from Surface Owner's wells or ponds without "
              "separate written agreement and shall repair any damage to existing water sources."),
        ("blank", ""),
        ("p", f"SURFACE OWNER: {d['lessor']}"),
        ("p", f"OPERATOR: {d['lessee']}"),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_easement(d) -> list[tuple[str, str]]:
    return [
        ("h1", "Right-of-Way and Pipeline Easement"),
        ("p", f"This Right-of-Way and Easement Agreement is made {d['effective_date']} by "
              f"{d['grantor']} (\"Grantor\") in favor of {d['grantee']} (\"Grantee\")."),
        ("h2", "Grant of Easement"),
        ("p", f"For consideration of {d['consideration']}, Grantor grants Grantee a "
              f"{d['width']}-foot wide easement and right-of-way to construct, operate, maintain, "
              f"inspect, replace, and remove one or more pipelines for the transportation of oil, "
              f"gas, water, and related substances, with appurtenant valves and cathodic protection."),
        ("h2", "Location"),
        ("p", f"The easement crosses the following land: {d['legal']}, {d['county']} County, "
              f"{d['state']}, the centerline being as staked and as shown on the plat attached as "
              f"Exhibit A. The right-of-way traverses approximately {d['rods']} rods "
              f"({d['acres']} acres of working space during construction)."),
        ("h2", "Depth and Restoration"),
        ("p", "Pipelines shall be buried a minimum of 48 inches below the surface and below the "
              "plow depth of cultivated land. Grantee shall restore the surface, repair fences and "
              "drainage, and pay for growing crops and timber actually damaged."),
        ("h2", "Term"),
        ("p", "This easement shall continue so long as the right-of-way is used for the purposes "
              "granted; abandonment for twenty-four (24) consecutive months terminates the grant and "
              "title reverts to Grantor."),
        ("blank", ""),
        ("p", f"GRANTOR: {d['grantor']}"),
    ] + _notary(d["state"], "County", d["county"], d["grantor"])


def t_title_opinion(d) -> list[tuple[str, str]]:
    return [
        ("h1", "Drilling Title Opinion"),
        ("p", f"TO: {d['lessee']}"),
        ("p", f"FROM: {d['examiner']}"),
        ("p", f"DATE: {d['effective_date']}"),
        ("p", f"RE: {d['legal']}, {d['county']} County, {d['state']} ({d['acres']} acres)"),
        ("blank", ""),
        ("h2", "Scope of Examination"),
        ("p", f"At your request, I have examined an abstract of title covering the captioned lands, "
              f"comprising {d['abstract_entries']} numbered entries certified to {d['cert_date']} by "
              f"{d['abstractor']}. This opinion is limited to record title and is rendered for the "
              f"sole use of the addressee."),
        ("h2", "Marketable Title"),
        ("p", f"Subject to the comments and requirements below, marketable title to the mineral "
              f"estate is vested as follows: {d['mineral_owner']} owns the executive mineral "
              f"interest, subject to a non-participating royalty of {d['npri']} held by "
              f"{d['npri_owner']}."),
        ("h2", "Comments"),
        ("p", "Comment 1: The 1987 probate of the Estate of A. Henderson did not include a recorded "
              "order admitting the will; heirship is presumed from the family settlement agreement at "
              "Book 412, Page 88."),
        ("p", "Comment 2: A prior oil and gas lease appears expired by its own terms for lack of "
              "production but no release of record was located."),
        ("h2", "Requirements"),
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


def t_grazing_lease(d) -> list[tuple[str, str]]:
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
              "to mineral lessees; Lessee's rent shall be equitably reduced for acreage taken out of "
              "grazing by such operations. Hunting rights are reserved to Lessor."),
        ("blank", ""),
        ("p", f"LESSOR: {d['lessor']}"),
        ("p", f"LESSEE: {d['lessee']}"),
    ] + _notary(d["state"], "County", d["county"], d["lessor"])


def t_amendment(d) -> list[tuple[str, str]]:
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


TEMPLATES = {
    "oil_gas_lease": t_oil_gas_lease,
    "paidup_modern": t_paidup_modern,
    "metes_bounds_lease": t_metes_bounds_lease,
    "memorandum": t_memorandum,
    "mineral_deed": t_mineral_deed,
    "royalty_deed": t_royalty_deed,
    "warranty_deed": t_warranty_deed,
    "quitclaim": t_quitclaim,
    "surface_use": t_surface_use,
    "easement": t_easement,
    "title_opinion": t_title_opinion,
    "grazing_lease": t_grazing_lease,
    "amendment": t_amendment,
}

# ---------------------------------------------------------------------------
# The corpus: 24 synthetic documents. Coordinates are the approximate county
# seat / tract centroid (real places) so each maps to an actual US location.
# ---------------------------------------------------------------------------

DOCS = [
    {
        "id": "01-ogl-midland-tx", "template": "oil_gas_lease",
        "doc_type": "Oil and Gas Lease", "form_no": "TX-88-PB",
        "state": "Texas", "county": "Midland", "municipality": "Midland",
        "lat": 31.9973, "lon": -102.0779, "system": "Texas abstract/block-section",
        "legal": "Section 14, Block 39, T-2-S, Texas & Pacific Ry. Co. Survey, Abstract No. 1187",
        "acres": "640.00",
        "lessor": "Margaret A. Caldwell, a single woman",
        "lessee": "Llano Estacado Operating, LLC",
        "effective_date": "January 15, 2025", "term": "three (3) years",
        "royalty": "one-fourth (1/4)", "bonus": "$1,500.00 per net mineral acre",
        "shut_in": "$50.00",
    },
    {
        "id": "02-ogl-reeves-tx", "template": "paidup_modern",
        "doc_type": "Oil and Gas Lease",
        "state": "Texas", "county": "Reeves", "municipality": "Pecos",
        "lat": 31.4229, "lon": -103.4932, "system": "Texas abstract/block-section",
        "legal": "Section 22, Block 13, H&GN RR Co. Survey, Abstract No. 2204",
        "acres": "320.00",
        "lessor": "The Holloway Family Trust dated June 3, 1998",
        "lessee": "Delaware Basin Resources, LP",
        "effective_date": "March 1, 2025", "term": "five (5) years",
        "royalty": "22.5% (9/40)", "bonus": "$2,250.00 per net mineral acre",
    },
    {
        "id": "03-ogl-lea-nm", "template": "oil_gas_lease",
        "doc_type": "Oil and Gas Lease", "form_no": "NM-PB-01",
        "state": "New Mexico", "county": "Lea", "municipality": "Lovington",
        "lat": 32.9445, "lon": -103.3486, "system": "PLSS (New Mexico P.M.)",
        "legal": "Township 20 South, Range 37 East, N.M.P.M., Section 16: SE/4",
        "acres": "160.00",
        "lessor": "James R. and Linda S. Whitaker, husband and wife",
        "lessee": "Mesa Verde Resources, LP",
        "effective_date": "February 10, 2025", "term": "three (3) years",
        "royalty": "three-sixteenths (3/16)", "bonus": "$1,000.00 per net mineral acre",
        "shut_in": "$25.00",
    },
    {
        "id": "04-ogl-eddy-nm", "template": "oil_gas_lease",
        "doc_type": "Oil and Gas Lease", "form_no": "NM-PB-02",
        "state": "New Mexico", "county": "Eddy", "municipality": "Carlsbad",
        "lat": 32.4207, "lon": -104.2288, "system": "PLSS (New Mexico P.M.)",
        "legal": "Township 22 South, Range 28 East, N.M.P.M., Section 9: N/2",
        "acres": "320.00",
        "lessor": "Pecos Valley Land Company, a New Mexico corporation",
        "lessee": "Mesa Verde Resources, LP",
        "effective_date": "April 22, 2025", "term": "five (5) years",
        "royalty": "one-fifth (1/5)", "bonus": "$1,750.00 per net mineral acre",
        "shut_in": "$50.00",
    },
    {
        "id": "05-ogl-mckenzie-nd", "template": "paidup_modern",
        "doc_type": "Oil and Gas Lease",
        "state": "North Dakota", "county": "McKenzie", "municipality": "Watford City",
        "lat": 47.8022, "lon": -103.2832, "system": "PLSS (5th P.M.)",
        "legal": "Township 150 North, Range 98 West, 5th P.M., Section 22: S/2",
        "acres": "320.00",
        "lessor": "Arnold T. Bergstrom and Carol J. Bergstrom, as joint tenants",
        "lessee": "Bakken Ridge Energy, Inc.",
        "effective_date": "May 5, 2024", "term": "five (5) years",
        "royalty": "18.75% (3/16)", "bonus": "$900.00 per net mineral acre",
    },
    {
        "id": "06-ogl-weld-co", "template": "paidup_modern",
        "doc_type": "Oil and Gas Lease",
        "state": "Colorado", "county": "Weld", "municipality": "Greeley",
        "lat": 40.4233, "lon": -104.7091, "system": "PLSS (6th P.M.)",
        "legal": "Township 6 North, Range 63 West, 6th P.M., Section 9: SW/4",
        "acres": "160.00",
        "lessor": "Front Range Cattle Co., LLC",
        "lessee": "Front Range Petroleum, LLC",
        "effective_date": "September 12, 2024", "term": "three (3) years",
        "royalty": "20% (1/5)", "bonus": "$1,200.00 per net mineral acre",
    },
    {
        "id": "07-ogl-kingfisher-ok", "template": "oil_gas_lease",
        "doc_type": "Oil and Gas Lease", "form_no": "OK-88",
        "state": "Oklahoma", "county": "Kingfisher", "municipality": "Kingfisher",
        "lat": 35.8620, "lon": -97.9320, "system": "PLSS (Indian Meridian)",
        "legal": "Township 16 North, Range 7 West, I.M., Section 19: NE/4",
        "acres": "160.00",
        "lessor": "Estate of Harlan W. Dietrich, by Susan Dietrich, Personal Representative",
        "lessee": "Red River Minerals, LLC",
        "effective_date": "July 8, 2024", "term": "three (3) years",
        "royalty": "three-sixteenths (3/16)", "bonus": "$800.00 per net mineral acre",
        "shut_in": "$25.00",
    },
    {
        "id": "08-ogl-washington-pa", "template": "metes_bounds_lease",
        "doc_type": "Oil and Gas Lease",
        "state": "Pennsylvania", "county": "Washington", "municipality": "Amwell Township",
        "township": "Amwell Township",
        "lat": 40.1742, "lon": -80.2462, "system": "metes and bounds",
        "legal": ("BEGINNING at an iron pin at the northwest corner of lands now or formerly of "
                  "Reynolds; thence S 82 deg E 1,420 feet to a post; thence S 6 deg W 3,160 feet "
                  "along lands of Maple Run to a stone; thence N 84 deg W 1,390 feet to a white oak; "
                  "thence N 5 deg E 3,090 feet to the place of beginning"),
        "acres": "112.40", "source_deed": "Deed Book 1123, Page 456",
        "lessor": "Robert E. Stanton and Patricia Stanton, his wife",
        "lessee": "Keystone Shale Partners, LP",
        "effective_date": "October 1, 2024", "term": "five (5) years",
        "royalty": "one-eighth (1/8)", "oil_royalty": "one-eighth (1/8)",
        "bonus": "$3,000.00 per net acre",
    },
    {
        "id": "09-ogl-greene-pa", "template": "metes_bounds_lease",
        "doc_type": "Oil and Gas Lease",
        "state": "Pennsylvania", "county": "Greene", "municipality": "Morgan Township",
        "township": "Morgan Township",
        "lat": 39.8962, "lon": -80.1811, "system": "metes and bounds",
        "legal": ("BEGINNING at a fence post corner; thence along Township Road 388 N 71 deg E "
                  "980 feet; thence S 19 deg E 2,610 feet to a marked hickory; thence S 70 deg W "
                  "1,005 feet; thence N 18 deg W 2,640 feet to the point of beginning"),
        "acres": "61.80", "source_deed": "Instrument No. 2016-004412",
        "lessor": "Greene Hills Land Holdings, LLC",
        "lessee": "Keystone Shale Partners, LP",
        "effective_date": "November 14, 2024", "term": "five (5) years",
        "royalty": "fifteen percent (15%)", "oil_royalty": "one-eighth (1/8)",
        "bonus": "$2,500.00 per net acre",
    },
    {
        "id": "10-ogl-doddridge-wv", "template": "metes_bounds_lease",
        "doc_type": "Oil and Gas Lease",
        "state": "West Virginia", "county": "Doddridge", "municipality": "McClellan District",
        "township": "McClellan District",
        "lat": 39.2690, "lon": -80.7762, "system": "metes and bounds",
        "legal": ("Beginning at a sugar maple on the bank of Middle Island Creek; thence with the "
                  "creek N 44 deg E 62 poles; thence leaving the creek S 60 deg E 138 poles to a "
                  "stone; thence S 40 deg W 70 poles; thence N 58 deg W 142 poles to the beginning"),
        "acres": "88.25", "source_deed": "Deed Book 244, Page 19",
        "lessor": "Floyd and Wanda Carpenter, husband and wife",
        "lessee": "Allegheny Gas Company",
        "effective_date": "August 19, 2024", "term": "five (5) years",
        "royalty": "one-eighth (1/8)", "oil_royalty": "one-eighth (1/8)",
        "bonus": "$1,800.00 per net acre",
    },
    {
        "id": "11-ogl-belmont-oh", "template": "oil_gas_lease",
        "doc_type": "Oil and Gas Lease", "form_no": "OH-PB",
        "state": "Ohio", "county": "Belmont", "municipality": "Mead Township",
        "county_label": "County",
        "lat": 40.0801, "lon": -80.9009, "system": "metes and bounds (sectionalized)",
        "legal": "Situate in Mead Township, Section 18, being 87.60 acres out of a 160-acre original tract",
        "acres": "87.60",
        "lessor": "The Novak Revocable Living Trust",
        "lessee": "Buckeye Utica Operating, LLC",
        "effective_date": "June 3, 2024", "term": "five (5) years",
        "royalty": "one-sixth (1/6)", "bonus": "$4,000.00 per net acre",
        "shut_in": "$100.00",
    },
    {
        "id": "12-ogl-caddo-la", "template": "oil_gas_lease",
        "doc_type": "Oil and Gas Lease", "form_no": "LA-BR",
        "state": "Louisiana", "county": "Caddo", "municipality": "Shreveport",
        "county_label": "Parish",
        "lat": 32.5252, "lon": -93.7502, "system": "PLSS (Louisiana Meridian)",
        "legal": "Section 24, Township 18 North, Range 15 West, Louisiana Meridian",
        "acres": "80.00",
        "lessor": "Beaulieu Land & Timber, L.L.C.",
        "lessee": "Caddo Pine Energy, LLC",
        "effective_date": "December 2, 2024", "term": "three (3) years",
        "royalty": "one-fourth (1/4)", "bonus": "$600.00 per net mineral acre",
        "shut_in": "$50.00",
    },
    {
        "id": "13-ogl-campbell-wy", "template": "paidup_modern",
        "doc_type": "Oil and Gas Lease",
        "state": "Wyoming", "county": "Campbell", "municipality": "Gillette",
        "lat": 44.2911, "lon": -105.5022, "system": "PLSS (6th P.M.)",
        "legal": "Township 49 North, Range 71 West, 6th P.M., Section 5: Lots 3 and 4, S/2 NW/4",
        "acres": "158.40",
        "lessor": "Powder River Grazing Association",
        "lessee": "Powder River Resources, LLC",
        "effective_date": "April 1, 2024", "term": "five (5) years",
        "royalty": "one-sixth (1/6)", "bonus": "$350.00 per net mineral acre",
    },
    {
        "id": "14-ogl-kern-ca", "template": "oil_gas_lease",
        "doc_type": "Oil and Gas Lease", "form_no": "CA-88",
        "state": "California", "county": "Kern", "municipality": "Bakersfield",
        "lat": 35.3733, "lon": -119.0187, "system": "PLSS (Mount Diablo Meridian)",
        "legal": "Township 30 South, Range 28 East, M.D.M., Section 12: NW/4",
        "acres": "160.00",
        "lessor": "San Joaquin Heritage Farms, Inc.",
        "lessee": "San Joaquin Oil Company",
        "effective_date": "February 28, 2025", "term": "three (3) years",
        "royalty": "one-sixth (1/6)", "bonus": "$1,100.00 per net mineral acre",
        "shut_in": "$50.00",
    },
    {
        "id": "15-memo-karnes-tx", "template": "memorandum",
        "doc_type": "Memorandum of Oil and Gas Lease",
        "state": "Texas", "county": "Karnes", "municipality": "Karnes City",
        "lat": 28.8853, "lon": -97.9003, "system": "Texas abstract/block-section",
        "legal": "A-456, J. de la Garza Survey, Abstract No. 456",
        "acres": "210.50",
        "lessor": "Esperanza Ranch Partners, Ltd.",
        "lessee": "Eagle Ford Operating Company",
        "effective_date": "January 9, 2025", "term": "three (3) years",
        "royalty": "one-fourth (1/4)",
        "recording": {
            "instrument_no": "2025-0000487", "book": "1042", "page": "330",
            "recorded": "January 14, 2025", "recorder": "Karnes County Clerk",
        },
    },
    {
        "id": "16-mineral-deed-stephens-ok", "template": "mineral_deed",
        "doc_type": "Mineral Deed",
        "state": "Oklahoma", "county": "Stephens", "municipality": "Duncan",
        "lat": 34.5023, "lon": -97.9578, "system": "PLSS (Indian Meridian)",
        "legal": "Township 1 South, Range 6 West, I.M., Section 27: SW/4",
        "acres": "160.00", "interest": "one-half (1/2)",
        "grantor": "Dorothy M. Albright, a widow",
        "grantee": "Chisholm Trail Royalties, LLC",
        "consideration": "Ten Dollars and other good and valuable consideration",
        "effective_date": "March 18, 2025",
    },
    {
        "id": "17-royalty-deed-reagan-tx", "template": "royalty_deed",
        "doc_type": "Royalty Deed",
        "state": "Texas", "county": "Reagan", "municipality": "Big Lake",
        "lat": 31.1932, "lon": -101.4663, "system": "Texas abstract/block-section",
        "legal": "Section 7, Block 2, H&TC RR Co. Survey, Abstract No. 89",
        "acres": "640.00", "interest": "a 1/32 of 8/8",
        "grantor": "University Lands Heritage Trust",
        "grantee": "Santa Rita Royalty Company",
        "consideration": "$48,000.00",
        "effective_date": "May 30, 2025",
    },
    {
        "id": "18-warranty-deed-garfield-co", "template": "warranty_deed",
        "doc_type": "General Warranty Deed",
        "state": "Colorado", "county": "Garfield", "municipality": "Glenwood Springs",
        "lat": 39.5505, "lon": -107.3248, "system": "PLSS (6th P.M.)",
        "legal": "Township 6 South, Range 92 West, 6th P.M., Section 14: NE/4 SE/4",
        "acres": "40.00", "reservation": "one-half (1/2)",
        "grantor": "Roaring Fork Holdings, LLC",
        "grantee": "Caleb and Marie Donnelly, as joint tenants",
        "consideration": "$385,000.00",
        "effective_date": "April 11, 2025",
    },
    {
        "id": "19-quitclaim-rio-arriba-nm", "template": "quitclaim",
        "doc_type": "Quitclaim Deed",
        "state": "New Mexico", "county": "Rio Arriba", "municipality": "Tierra Amarilla",
        "lat": 36.7045, "lon": -106.5464, "system": "Spanish/Mexican land grant (tract)",
        "legal": "A portion of the Tierra Amarilla Land Grant, Tract 7-B, as shown on the "
                 "amended grant plat of record",
        "acres": "35.75",
        "grantor": "Ramon C. Trujillo",
        "grantee": "The Trujillo Family, LLC",
        "consideration": "$10.00 and natural love and affection",
        "effective_date": "June 1, 2025",
    },
    {
        "id": "20-surface-use-dunn-nd", "template": "surface_use",
        "doc_type": "Surface Use and Damage Agreement",
        "state": "North Dakota", "county": "Dunn", "municipality": "Killdeer",
        "lat": 47.3722, "lon": -102.7521, "system": "PLSS (5th P.M.)",
        "legal": "Township 146 North, Range 95 West, 5th P.M., Section 8: N/2",
        "acres": "320.00",
        "lessor": "Knutson Brothers Farm Partnership",
        "lessee": "Bakken Ridge Energy, Inc.",
        "effective_date": "July 15, 2024",
        "surface_payment": "$25,000.00", "road_payment": "$30.00",
    },
    {
        "id": "21-easement-dewitt-tx", "template": "easement",
        "doc_type": "Right-of-Way and Pipeline Easement",
        "state": "Texas", "county": "DeWitt", "municipality": "Cuero",
        "lat": 29.0938, "lon": -97.2886, "system": "Texas abstract/block-section",
        "legal": "out of the William Ponton Survey, Abstract No. 36",
        "acres": "4.20", "width": "50", "rods": "402",
        "grantor": "Cuero Creek Ranch, Ltd.",
        "grantee": "Guadalupe Midstream Partners, LP",
        "consideration": "$60,300.00 ($150.00 per rod)",
        "effective_date": "August 4, 2024",
    },
    {
        "id": "22-title-opinion-loving-tx", "template": "title_opinion",
        "doc_type": "Drilling Title Opinion",
        "state": "Texas", "county": "Loving", "municipality": "Mentone",
        "lat": 31.7060, "lon": -103.5977, "system": "Texas abstract/block-section",
        "legal": "Section 30, Block C-24, PSL Survey, Abstract No. 612",
        "acres": "640.00",
        "lessee": "Delaware Basin Resources, LP",
        "examiner": "T. Lindqvist, Attorney at Law, Lindqvist & Reyes PLLC",
        "abstractor": "Trans-Pecos Abstract Co.",
        "abstract_entries": "63", "cert_date": "March 31, 2025",
        "mineral_owner": "Mentone Minerals, Ltd. (undivided 7/8)",
        "npri": "1/8 of 8/8", "npri_owner": "the Henderson Family",
        "effective_date": "April 28, 2025",
    },
    {
        "id": "23-grazing-lease-carbon-mt", "template": "grazing_lease",
        "doc_type": "Grazing and Ranch Lease",
        "state": "Montana", "county": "Carbon", "municipality": "Red Lodge",
        "lat": 45.1863, "lon": -109.2466, "system": "PLSS (Montana P.M.)",
        "legal": "Township 7 South, Range 20 East, M.P.M., Sections 4 and 9 (all)",
        "acres": "1,280.00",
        "lessor": "Beartooth Mountain Land Trust",
        "lessee": "Rock Creek Cattle Company",
        "effective_date": "March 1, 2025", "term": "five (5) years",
        "rent": "$18.00 per acre", "rent_schedule": "annually in advance on March 1",
        "aum": "260",
    },
    {
        "id": "24-amendment-lea-nm", "template": "amendment",
        "doc_type": "Lease Amendment and Extension",
        "state": "New Mexico", "county": "Lea", "municipality": "Hobbs",
        "lat": 32.7026, "lon": -103.1360, "system": "PLSS (New Mexico P.M.)",
        "legal": "Township 19 South, Range 38 East, N.M.P.M., Section 33: W/2",
        "acres": "320.00",
        "lessor": "James R. and Linda S. Whitaker, husband and wife",
        "lessee": "Mesa Verde Resources, LP",
        "orig_date": "February 10, 2022",
        "effective_date": "February 1, 2025", "term": "two (2) years",
        "royalty": "one-fourth (1/4)", "bonus": "$500.00 per net mineral acre",
        "recording": {"instrument_no": "2022-0001998", "book": "880", "page": "145"},
    },
]


def render_all() -> None:
    LEASES_DIR.mkdir(parents=True, exist_ok=True)
    manifest = []
    for d in DOCS:
        blocks = TEMPLATES[d["template"]](d)
        md_path = LEASES_DIR / f"{d['id']}.md"
        pdf_path = LEASES_DIR / f"{d['id']}.pdf"
        md_path.write_text(to_markdown(blocks), encoding="utf-8")
        pdf_path.write_bytes(build_pdf(to_lines(blocks)))

        manifest.append({
            "id": d["id"],
            "markdown": f"leases/{d['id']}.md",
            "pdf": f"leases/{d['id']}.pdf",
            "doc_type": d["doc_type"],
            "state": d["state"],
            "county": d["county"],
            "county_label": d.get("county_label", "County"),
            "municipality": d.get("municipality"),
            "latitude": d["lat"],
            "longitude": d["lon"],
            "legal_description_system": d["system"],
            "legal_description": d["legal"],
            "acres": d.get("acres"),
            # answer-key fields for extraction/retrieval testing (present where applicable)
            "lessor_or_grantor": d.get("lessor") or d.get("grantor"),
            "lessee_or_grantee": d.get("lessee") or d.get("grantee"),
            "effective_date": d.get("effective_date"),
            "royalty": d.get("royalty"),
            "bonus": d.get("bonus"),
            "primary_term": d.get("term"),
        })

    (OUT_DIR / "manifest.json").write_text(
        json.dumps({"documents": manifest}, indent=2) + "\n", encoding="utf-8"
    )
    print(f"Generated {len(DOCS)} documents (.md + .pdf) in {LEASES_DIR}")
    print(f"Wrote answer key: {OUT_DIR / 'manifest.json'}")


if __name__ == "__main__":
    render_all()
