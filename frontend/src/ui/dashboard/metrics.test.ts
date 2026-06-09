import { describe, expect, it } from 'vitest'
import type { DocumentSummary, ExtractedField } from '../../api/types'
import {
  documentsByState,
  documentsByStateCounty,
  expirationBucketCounts,
  findTermEnd,
  ingestByHour,
  parseTermDuration,
  needsReview,
  needsReviewDocuments,
  summarize,
  upcomingExpirations,
} from './metrics'

function field(name: string, value: string): ExtractedField {
  return { name, value, sourceChunkId: null }
}

function doc(overrides: Partial<DocumentSummary> = {}): DocumentSummary {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    fileName: overrides.fileName ?? 'lease.pdf',
    status: overrides.status ?? 'ready',
    contentType: overrides.contentType ?? 'application/pdf',
    chunkCount: overrides.chunkCount ?? 3,
    fields: overrides.fields ?? [field('Lessee', 'Acme Minerals LLC'), field('EffectiveDate', '2026-01-15')],
    ingestedAt: overrides.ingestedAt ?? '2026-06-08T12:00:00.000Z',
  }
}

describe('summarize', () => {
  it('counts documents, chunks, distinct locations, and latest ingest', () => {
    const docs = [
      doc({ chunkCount: 4, fields: [field('State', 'Texas'), field('County', 'Midland'), field('Lessee', 'A')], ingestedAt: '2026-06-01T00:00:00.000Z' }),
      doc({ chunkCount: 6, fields: [field('State', 'texas'), field('County', 'Kern'), field('Lessor', 'B')], ingestedAt: '2026-06-09T00:00:00.000Z' }),
    ]
    const s = summarize(docs)
    expect(s.totalDocuments).toBe(2)
    expect(s.totalChunks).toBe(10)
    expect(s.distinctStates).toBe(1) // "Texas" / "texas" collapse case-insensitively
    expect(s.distinctCounties).toBe(2)
    expect(s.latestIngest?.toISOString()).toBe('2026-06-09T00:00:00.000Z')
  })

  it('handles an empty corpus', () => {
    const s = summarize([])
    expect(s).toEqual({
      totalDocuments: 0,
      totalChunks: 0,
      distinctCounties: 0,
      distinctStates: 0,
      needsReviewCount: 0,
      latestIngest: null,
    })
  })
})

describe('needsReview', () => {
  it('flags no-fields, empty-valued, and party-and-date-less documents', () => {
    expect(needsReview(doc({ fields: [] }))).toBe(true)
    expect(needsReview(doc({ fields: [field('Lessee', '')] }))).toBe(true)
    expect(needsReview(doc({ fields: [field('Royalty', '3/16')] }))).toBe(true) // no party, no date
  })

  it('passes a well-extracted document', () => {
    expect(needsReview(doc({ fields: [field('Lessee', 'Acme'), field('EffectiveDate', '2026-01-15')] }))).toBe(false)
  })

  it('needsReviewDocuments filters the set', () => {
    const good = doc({ fields: [field('Lessee', 'Acme'), field('EffectiveDate', '2026-01-15')] })
    const bad = doc({ fields: [] })
    expect(needsReviewDocuments([good, bad])).toEqual([bad])
  })
})

describe('documentsByState', () => {
  it('groups by state value and buckets missing as Unknown, sorted by count desc', () => {
    const docs = [
      doc({ fields: [field('State', 'Texas')] }),
      doc({ fields: [field('State', 'Texas')] }),
      doc({ fields: [field('Lessee', 'No state here')] }),
    ]
    expect(documentsByState(docs)).toEqual([
      { label: 'Texas', count: 2 },
      { label: 'Unknown', count: 1 },
    ])
  })
})

describe('ingestByHour', () => {
  it('buckets by UTC hour, sorted ascending', () => {
    const docs = [
      doc({ ingestedAt: '2026-06-09T03:44:30.000Z' }),
      doc({ ingestedAt: '2026-06-09T02:53:00.000Z' }),
      doc({ ingestedAt: '2026-06-09T02:53:33.000Z' }),
    ]
    expect(ingestByHour(docs)).toEqual([
      { hour: '2026-06-09T02:00', count: 2 },
      { hour: '2026-06-09T03:00', count: 1 },
    ])
  })

  it('skips documents with an unparseable ingestedAt', () => {
    expect(ingestByHour([doc({ ingestedAt: 'not-a-date' })])).toEqual([])
  })
})

describe('documentsByStateCounty', () => {
  it('counts documents per (state, county), skipping those missing either field', () => {
    const docs = [
      doc({ fields: [field('State', 'Texas'), field('County', 'Reeves')] }),
      doc({ fields: [field('State', 'Texas'), field('County', 'Reeves')] }),
      doc({ fields: [field('State', 'New Mexico'), field('County', 'Lea')] }),
      doc({ fields: [field('County', 'Orphan')] }), // no state — skipped
      doc({ fields: [] }), // nothing — skipped
    ]
    expect(documentsByStateCounty(docs)).toEqual([
      { state: 'Texas', county: 'Reeves', count: 2 },
      { state: 'New Mexico', county: 'Lea', count: 1 },
    ])
  })
})

describe('parseTermDuration', () => {
  it('parses the corpus phrasings (spelled-out + parenthetical digit + unit)', () => {
    expect(parseTermDuration('five (5) years')).toEqual({ years: 5, months: 0 })
    expect(parseTermDuration('three (3) years from the effective date')).toEqual({ years: 3, months: 0 })
    expect(parseTermDuration('two years')).toEqual({ years: 2, months: 0 })
    expect(parseTermDuration('36 months')).toEqual({ years: 0, months: 36 })
    expect(parseTermDuration('2 yrs')).toEqual({ years: 2, months: 0 })
  })

  it('returns null without a recognizable unit or number', () => {
    expect(parseTermDuration('5')).toBeNull() // no unit
    expect(parseTermDuration('paid-up lease')).toBeNull() // no number/unit
    expect(parseTermDuration('zero years')).toBeNull() // no positive number
  })
})

describe('findTermEnd', () => {
  it('prefers an explicit expiration/term-end date field', () => {
    const result = findTermEnd(doc({ fields: [field('ExpirationDate', '2027-01-01')] }))
    expect(result?.basis).toBe('explicit')
    // Parsed as a local calendar date (not UTC), so assert on local components.
    expect([result?.date.getFullYear(), result?.date.getMonth(), result?.date.getDate()]).toEqual([2027, 0, 1])
  })

  it('derives the primary-term end from effective date + primary term', () => {
    const result = findTermEnd(
      doc({ fields: [field('EffectiveDate', 'May 5, 2024'), field('PrimaryTerm', 'five (5) years')] }),
    )
    expect(result?.basis).toBe('derived')
    expect(result?.date.getFullYear()).toBe(2029)
    expect(result?.date.getMonth()).toBe(4) // May (0-indexed)
  })

  it('is null when only one of effective date / primary term is present', () => {
    expect(findTermEnd(doc({ fields: [field('EffectiveDate', '2026-01-15')] }))).toBeNull()
    expect(findTermEnd(doc({ fields: [field('PrimaryTerm', 'five (5) years')] }))).toBeNull()
  })
})

describe('expirations', () => {
  it('buckets upcoming expirations relative to now', () => {
    const now = new Date('2026-06-09T00:00:00.000Z')
    const docs = [
      doc({ fileName: 'expired.pdf', fields: [field('ExpirationDate', '2026-01-01')] }),
      doc({ fileName: 'soon.pdf', fields: [field('Lease Term End', '2026-06-20')] }),
      doc({ fileName: 'later.pdf', fields: [field('ExpirationDate', '2027-01-01')] }),
      doc({ fileName: 'nodate.pdf', fields: [field('Lessee', 'Acme')] }),
    ]
    const counts = expirationBucketCounts(docs, now)
    expect(counts).toEqual({ expired: 1, within30: 1, within60: 0, within90: 0, later: 1 })

    const upcoming = upcomingExpirations(docs, now)
    expect(upcoming.map((u) => u.document.fileName)).toEqual(['expired.pdf', 'soon.pdf', 'later.pdf'])
    expect(upcoming[1].bucket).toBe('within30')
  })

  it('returns empty when no document carries a term/expiration field', () => {
    expect(upcomingExpirations([doc({ fields: [field('Lessee', 'Acme')] })])).toEqual([])
  })
})
