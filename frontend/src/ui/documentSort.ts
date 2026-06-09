import type { DocumentSummary } from '../api/types'

// Pure client-side sorting for the documents table. All rows are already in memory (spec 0007 — no new
// fetch), so sorting is a local reorder. Kept separate from the table component so the comparators are
// unit-tested directly.

export type SortKey = 'file' | 'status' | 'chunks' | 'fields' | 'ingested'
export type SortDir = 'asc' | 'desc'

/** Comparator value for a document under a given column — numbers for numeric columns, strings otherwise. */
function sortValue(doc: DocumentSummary, key: SortKey): number | string {
  switch (key) {
    case 'file':
      return doc.fileName.toLowerCase()
    case 'status':
      return doc.status.toLowerCase()
    case 'chunks':
      return doc.chunkCount
    case 'fields':
      return doc.fields.length
    case 'ingested': {
      const ms = Date.parse(doc.ingestedAt)
      // Unparseable dates sort last (treated as -Infinity flips under desc, so use a stable sentinel).
      return Number.isNaN(ms) ? Number.NEGATIVE_INFINITY : ms
    }
  }
}

function compare(a: number | string, b: number | string): number {
  if (typeof a === 'number' && typeof b === 'number') {
    return a - b
  }
  return String(a).localeCompare(String(b))
}

/**
 * Returns a new array of `docs` sorted by `key`/`dir`. Stable: rows comparing equal keep their input order.
 * Never mutates the input.
 */
export function sortDocuments(
  docs: readonly DocumentSummary[],
  key: SortKey,
  dir: SortDir,
): readonly DocumentSummary[] {
  const factor = dir === 'asc' ? 1 : -1
  return docs
    .map((doc, index) => ({ doc, index }))
    .sort((a, b) => {
      const primary = compare(sortValue(a.doc, key), sortValue(b.doc, key))
      return primary !== 0 ? primary * factor : a.index - b.index
    })
    .map((entry) => entry.doc)
}
