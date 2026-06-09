import type { DocumentSummary } from '../api/types'

// Pure client-side sorting for the documents table. All rows are already in memory, so sorting is a local
// reorder. Kept separate from the table component so the comparators are unit-tested directly.

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
      // Non-finite marks an unparseable date; the comparator pins those to the end in both directions.
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
      const av = sortValue(a.doc, key)
      const bv = sortValue(b.doc, key)
      // Invalid values (e.g. unparseable dates) always sort to the end, regardless of direction.
      const aInvalid = typeof av === 'number' && !Number.isFinite(av)
      const bInvalid = typeof bv === 'number' && !Number.isFinite(bv)
      if (aInvalid || bInvalid) {
        return aInvalid && bInvalid ? a.index - b.index : aInvalid ? 1 : -1
      }
      const primary = compare(av, bv)
      return primary !== 0 ? primary * factor : a.index - b.index
    })
    .map((entry) => entry.doc)
}
