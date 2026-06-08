# Sample land/title documents

A synthetic corpus for exercising the LandDoc ingest → extract → retrieve → ask
pipeline end to end. **36 documents** across **23 instrument types**, each emitted
as both readable Markdown (`leases/<id>.md`) and an ingestible text-based PDF
(`leases/<id>.pdf`).

Everything here is **synthetic** — party names, dates, dollar amounts, and
recording data are invented. But the document **structures** are patterned on real
instruments (clause inventories cross-checked against public legal references — see
*Fidelity & sources* below; **no real form's text is copied**), every document is
tied to a **real US place**, and it uses the **real legal-description system** for
that region. For PLSS tracts the `latitude`/`longitude` is **computed from the
township-range-section description** (not just the county seat), so the coordinate
matches the legal description — see *Geocoding* below.

## Geocoding — coordinates that match the description

- **19 PLSS tracts** (section-township-range states) are geolocated by `generate.py`'s
  built-in `plss_centroid`: PLSS is a regular 6-mile grid anchored at each principal
  meridian's documented initial point, with sections numbered in the standard
  boustrophedon pattern, so a Township/Range/Section/aliquot description converts to
  an approximate tract centroid (±~1–3 mi — it ignores convergence and correction
  lines). Each computed centroid lands **3–29 miles from its county seat**, i.e.
  inside the (large, western) county — `distance_to_county_seat_mi` in the manifest
  records this sanity check. `coordinate_basis` is `"PLSS tract centroid (computed…)"`.
- **17 non-PLSS docs** (Texas abstract/block-section, Appalachian metes-and-bounds,
  a Spanish land grant) have **no computable grid**, so their coordinate is a real
  in-county/town point, marked `coordinate_basis: "county/town approximate"`.

## What's covered

- **12 states** — TX, NM, ND, CO, OK, PA, WV, OH, LA, WY, CA, MT.
- **23 instrument types** spanning the real "land file":
  - *Leasing* — oil & gas lease (Producers-88 & modern paid-up & Appalachian forms),
    memorandum of lease, lease amendment/extension, **ratification**, **release of lease**.
  - *Conveyances* — mineral deed, royalty deed, general warranty deed, quitclaim deed,
    **assignment, bill of sale & conveyance (ABSC)**.
  - *Title & curative* — drilling title opinion, **division order title opinion (DOTO)**,
    **affidavit of heirship**, **order admitting will to probate**.
  - *Revenue* — **division order** (NADOA-style).
  - *Contracts* — **joint operating agreement (AAPL 610)**, **farmout agreement**,
    **area of mutual interest (AMI)**, surface use & damage agreement, pipeline ROW easement.
  - *Regulatory & cost* — **pooling order** (state commission), **authority for
    expenditure (AFE)**.
  - *Other* — grazing/ranch lease.
- **Tabular ("messy") documents** — the division order, AFE, JOA working-interest
  list, ABSC lease exhibit, and pooling-order election options render as tables, so
  the fixed-window chunker is tested on tabular as well as prose input.
- **Cross-document links** for corpus-wide retrieval — several documents describe the
  **same tract or estate** so an `/ask` query spans them: e.g. the *Henderson estate*
  (Loving Co., TX) appears in the title opinion, affidavit of heirship, probate order,
  and lease release; the *Whitaker tract* (Lea Co., NM) in a lease, amendment, and
  ratification; a *McKenzie Co., ND* section in a lease, JOA, and AFE.
- **Multiple legal-description systems** — PLSS section-township-range across seven
  meridians (5th, 6th, Indian, New Mexico, Mount Diablo, Montana, Louisiana), the
  **Texas abstract/block-section** survey system, **Appalachian metes-and-bounds**
  (PA/WV/OH), and a **Spanish land-grant** tract (NM).

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
- Division orders (NADOA model fields) —
  [NADOA model form booklet](https://nadoa.org/wp-content/uploads/2021/11/NADOA_Model_Form_DO_Booklet.pdf)
- AFE structure (dry-hole vs completion, IDC/tangible) —
  [Kingdom Exploration](https://www.kingdomexploration.com/?page=faq&slug=afe-oil-investment-authorization-expenditure-costs),
  [SPE glossary](https://onepetro.org/spe/general-information/1269/Authority-for-expenditures-AFE)
- JOA (AAPL Form 610 articles) —
  [Jackson Walker](https://www.jw.com/wp-content/uploads/2017/01/The-AAPL-Form-610-2015-Model-Form-Joint-Operating-Agreement.pdf),
  [Penn State 1989 JOA](https://www.e-education.psu.edu/ebf301/sites/www.e-education.psu.edu.ebf301/files/1989%20JOA%20(Clean).pdf)
- Affidavit of heirship (required contents) —
  [Winblad Law](https://winbladlaw.com/what-is-an-affidavit-of-heirship-using-it-for-oil-and-gas-mineral-rights/),
  [CourthouseDirect](https://info.courthousedirect.com/blog/bid/295150/what-s-an-affidavit-of-heirship-the-complete-guide)

> Still synthetic: structures are realistic and the geography is real, but the
> specific parties, descriptions, and dollar figures are invented and the documents
> are **not** legally precise or safe to treat as real records.
