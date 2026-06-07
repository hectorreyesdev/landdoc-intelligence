# Sample land/title documents

A synthetic corpus for exercising the LandDoc ingest → extract → retrieve → ask
pipeline end to end. **24 documents**, each emitted as both readable Markdown
(`leases/<id>.md`) and an ingestible text-based PDF (`leases/<id>.pdf`).

Everything here is **synthetic** — party names, dates, dollar amounts, and
recording data are invented. But the document **structures** are patterned on real
instruments (clause inventories cross-checked against public legal references — see
*Fidelity & sources* below; **no real form's text is copied**), every document is
tied to a **real US place**, and it uses the **real legal-description system** for
that region. For PLSS tracts the `latitude`/`longitude` is **computed from the
township-range-section description** (not just the county seat), so the coordinate
matches the legal description — see *Geocoding* below.

## Geocoding — coordinates that match the description

- **13 PLSS tracts** (section-township-range states) are geolocated by `generate.py`'s
  built-in `plss_centroid`: PLSS is a regular 6-mile grid anchored at each principal
  meridian's documented initial point, with sections numbered in the standard
  boustrophedon pattern, so a Township/Range/Section/aliquot description converts to
  an approximate tract centroid (±~1–3 mi — it ignores convergence and correction
  lines). Each computed centroid lands **3–29 miles from its county seat**, i.e.
  inside the (large, western) county — `distance_to_county_seat_mi` in the manifest
  records this sanity check. `coordinate_basis` is `"PLSS tract centroid (computed…)"`.
- **11 non-PLSS docs** (Texas abstract/block-section, Appalachian metes-and-bounds,
  a Spanish land grant) have **no computable grid**, so their coordinate is a real
  in-county/town point, marked `coordinate_basis: "county/town approximate"`.

## What's covered

- **12 states** — TX, NM, ND, CO, OK, PA, WV, OH, LA, WY, CA, MT.
- **9 instrument types** — oil & gas lease (the bulk), memorandum of lease,
  mineral deed, royalty deed, general warranty deed, quitclaim deed, surface use &
  damage agreement, pipeline right-of-way easement, drilling title opinion, grazing
  lease, and a lease amendment/extension.
- **Multiple legal-description systems** — PLSS section-township-range across six
  meridians (5th, 6th, Indian, New Mexico, Mount Diablo, Montana, Louisiana), the
  **Texas abstract/block-section** survey system, **Appalachian metes-and-bounds**
  (PA/WV/OH), and a **Spanish land-grant** tract (NM).
- **Different styles** — Producers-88 numbered-clause leases, modern paid-up
  leases, recital/habendum deeds, a memo-format title opinion with comments &
  requirements, WHEREAS-style agreements, and short-form recording memoranda.

## `manifest.json` — the answer key

Each entry carries the geocoding (`latitude`/`longitude`, `municipality`,
`legal_description_system`) **and** the ground-truth field values
(`lessor_or_grantor`, `lessee_or_grantee`, `royalty`, `bonus`, `effective_date`,
`primary_term`, `legal_description`, `acres`). Use it two ways:

1. **Map feature** — pin each document at its tract coordinates.
2. **Extraction/retrieval testing** — compare what the pipeline pulls or cites
   against the known-correct values.

## Regenerating

The files are generated, not hand-edited. Edit `generate.py` (the `DOCS` list or a
template) and re-run — it's dependency-free (the PDF writer is built in):

```bash
python3 samples/generate.py
```

To add documents, append a dict to `DOCS` with a `template` from `TEMPLATES` and
the relevant fields; coordinates should be the real tract/county-seat location.

## Loading them into the API

The ingest endpoint accepts one text-based PDF per `POST /documents` (no OCR —
scanned PDFs are out of scope). With the API running:

```bash
dotnet run --project backend/src/LandDoc.Api    # terminal 1
samples/upload.sh http://localhost:5000         # terminal 2
```

`upload.sh` POSTs every `leases/*.pdf` as multipart/form-data and prints each
response (the document id, extracted fields, and chunk count).

> Note: the in-memory vector store is process-lifetime only (ADR-0005) — restart
> the API and re-run `upload.sh` to rebuild the corpus.

## Fidelity & sources

The clause inventories were cross-checked against public legal/educational
references so the documents carry the provisions a real instrument of each type
would (e.g. leases include Mother Hubbard, continuous-operations/dry-hole, force
majeure, and Pugh clauses; the title opinion has a decimal tract-ownership
breakdown, a schedule of leases/encumbrances, and comments/requirements;
surface-use adds insurance/indemnity and interim reclamation; the easement splits
temporary-construction vs. permanent width and double-ditching). **All prose is
original/paraphrased — no form text is reproduced.** References consulted:

- Oil & gas lease clauses — [mineralwise.com](https://www.mineralwise.com/oil-gas-lease-forms),
  [Pheasant Energy](https://www.pheasantenergy.com/oil-and-gas-clauses/),
  [Guerra LLP](https://guerrallp.com/what-clauses-are-in-an-oil-gas-and-mineral-lease-and-what-do-they-mean)
- Mineral/warranty deed elements & the Duhig rule —
  [OU Law (special warranty)](https://digitalcommons.law.ou.edu/cgi/viewcontent.cgi?article=1073&context=onej),
  [CourthouseDirect (Duhig)](https://info.courthousedirect.com/blog/bid/306796/the-duhig-rule-mineral-rights-warranty-deeds)
- Title opinion structure/format —
  [Gray Reed (Yale)](https://www.grayreed.com/portalresource/SBTOilandGasTitleOpinions.pdf),
  [UARK ScholarWorks](https://scholarworks.uark.edu/cgi/viewcontent.cgi?article=1096&context=anrlaw)
- Surface use agreements —
  [Oliva Gibbs](https://oglawyers.com/practice-areas/surface-use-agreements/),
  [CCALT model](https://ccalt.org/wp-content/uploads/2021/02/Model_Surface_Use_Agmt_CCALT.pdf)
- Pipeline ROW easements —
  [OSU Ohioline](https://ohioline.osu.edu/factsheet/anr-33),
  [Penn State EARTH 109](https://www.e-education.psu.edu/earth109/node/683)

> Still synthetic: structures are realistic and the geography is real, but the
> specific parties, descriptions, and dollar figures are invented and the documents
> are **not** legally precise or safe to treat as real records.
