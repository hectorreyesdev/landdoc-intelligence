import { expect, it } from 'vitest'
import { sortDocuments, type SortKey } from './documentSort'
import type { DocumentSummary } from '../api/types'

function doc(over: Partial<DocumentSummary> & { id: string }): DocumentSummary {
  return {
    fileName: 'a.pdf',
    status: 'ready',
    contentType: 'application/pdf',
    chunkCount: 0,
    fields: [],
    ingestedAt: '2026-06-09T00:00:00+00:00',
    ...over,
  }
}

const docs: readonly DocumentSummary[] = [
  doc({ id: 'b', fileName: 'beta.pdf', status: 'ready', chunkCount: 6, fields: [{ name: 'x', value: '1', sourceChunkId: null }], ingestedAt: '2026-06-09T03:00:00+00:00' }),
  doc({ id: 'a', fileName: 'alpha.md', status: 'failed', chunkCount: 2, fields: [], ingestedAt: '2026-06-09T01:00:00+00:00' }),
  doc({ id: 'c', fileName: 'gamma.txt', status: 'ready', chunkCount: 4, fields: [{ name: 'x', value: '1', sourceChunkId: null }, { name: 'y', value: '2', sourceChunkId: null }], ingestedAt: '2026-06-09T02:00:00+00:00' }),
]

function ids(result: readonly DocumentSummary[]): string[] {
  return result.map((d) => d.id)
}

it('sorts by file name ascending and descending', () => {
  expect(ids(sortDocuments(docs, 'file', 'asc'))).toEqual(['a', 'b', 'c'])
  expect(ids(sortDocuments(docs, 'file', 'desc'))).toEqual(['c', 'b', 'a'])
})

it('sorts chunk count numerically (not lexically)', () => {
  expect(ids(sortDocuments(docs, 'chunks', 'asc'))).toEqual(['a', 'c', 'b'])
  expect(ids(sortDocuments(docs, 'chunks', 'desc'))).toEqual(['b', 'c', 'a'])
})

it('sorts by field count', () => {
  expect(ids(sortDocuments(docs, 'fields', 'asc'))).toEqual(['a', 'b', 'c'])
})

it('sorts by ingest time chronologically', () => {
  expect(ids(sortDocuments(docs, 'ingested', 'asc'))).toEqual(['a', 'c', 'b'])
})

it('sorts by status', () => {
  expect(ids(sortDocuments(docs, 'status', 'asc'))).toEqual(['a', 'b', 'c'])
})

it('is stable for equal keys (preserves input order)', () => {
  // 'b' and 'c' are both ready; input order is b before c, so they keep that order.
  const sorted = sortDocuments(docs, 'status', 'asc')
  expect(ids(sorted)).toEqual(['a', 'b', 'c'])
})

it('does not mutate the input array', () => {
  const before = ids(docs)
  sortDocuments(docs, 'file', 'desc')
  expect(ids(docs)).toEqual(before)
})

it('handles every key without throwing', () => {
  const keys: SortKey[] = ['file', 'status', 'chunks', 'fields', 'ingested']
  for (const key of keys) {
    expect(sortDocuments(docs, key, 'asc')).toHaveLength(3)
  }
})
