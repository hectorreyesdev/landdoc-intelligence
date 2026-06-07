# Sample land/title documents

A synthetic corpus for exercising the LandDoc ingest → extract → retrieve → ask
pipeline end to end. **24 documents**, each emitted as both readable Markdown
(`leases/<id>.md`) and an ingestible text-based PDF (`leases/<id>.pdf`).

Everything here is **synthetic**. Party names are invented. But every document is
tied to a **real US place** (county/parish, municipality, approximate lat/long in
`manifest.json`) and uses the **real legal-description system** for that region —
so the corpus maps to actual locations (for a future map feature) and stress-tests
the variety a real extractor would see.

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
