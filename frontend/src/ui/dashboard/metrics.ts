import type { DocumentSummary } from '../../api/types'

// Pure aggregations over the document list (spec 0007). Kept separate from the chart components so the
// logic is unit-tested directly — the Recharts SVG renders nothing under jsdom, so correctness lives here.

export interface DashboardSummary {
  readonly totalDocuments: number
  readonly totalChunks: number
  readonly distinctCounties: number
  readonly distinctStates: number
  readonly needsReviewCount: number
  readonly latestIngest: Date | null
}

export interface LabeledCount {
  readonly label: string
  readonly count: number
}

export interface DayCount {
  readonly date: string // YYYY-MM-DD
  readonly count: number
}

export type ExpirationBucket = 'expired' | 'within30' | 'within60' | 'within90' | 'later'

export interface UpcomingExpiration {
  readonly document: DocumentSummary
  readonly date: Date
  readonly daysUntil: number
  readonly bucket: ExpirationBucket
}

const PARTY_RE = /lessee|lessor|grantor|grantee|party|buyer|seller/i
const DATE_RE = /date/i
const COUNTY_RE = /county/i
const STATE_RE = /\bstate\b/i
const EXPIRATION_RE = /expir|term end|end date/i
const DAY_MS = 86_400_000

/** First non-empty value of a field whose name matches the pattern, or null. */
function fieldValue(doc: DocumentSummary, pattern: RegExp): string | null {
  for (const field of doc.fields) {
    if (pattern.test(field.name) && field.value.trim() !== '') {
      return field.value.trim()
    }
  }
  return null
}

/** A document needs review if it has no fields, any empty-valued field, or neither a party nor a date. */
export function needsReview(doc: DocumentSummary): boolean {
  if (doc.fields.length === 0) {
    return true
  }
  if (doc.fields.some((field) => field.value.trim() === '')) {
    return true
  }
  return fieldValue(doc, PARTY_RE) === null && fieldValue(doc, DATE_RE) === null
}

export function needsReviewDocuments(docs: readonly DocumentSummary[]): readonly DocumentSummary[] {
  return docs.filter(needsReview)
}

function distinctCount(docs: readonly DocumentSummary[], pattern: RegExp): number {
  const seen = new Set<string>()
  for (const doc of docs) {
    const value = fieldValue(doc, pattern)
    if (value !== null) {
      seen.add(value.toLowerCase())
    }
  }
  return seen.size
}

export function summarize(docs: readonly DocumentSummary[]): DashboardSummary {
  let totalChunks = 0
  let latestIngest: Date | null = null
  for (const doc of docs) {
    totalChunks += doc.chunkCount
    const ingested = new Date(doc.ingestedAt)
    if (!Number.isNaN(ingested.getTime()) && (latestIngest === null || ingested > latestIngest)) {
      latestIngest = ingested
    }
  }
  return {
    totalDocuments: docs.length,
    totalChunks,
    distinctCounties: distinctCount(docs, COUNTY_RE),
    distinctStates: distinctCount(docs, STATE_RE),
    needsReviewCount: docs.filter(needsReview).length,
    latestIngest,
  }
}

/** Count documents grouped by the value of a named field; missing values fall into `unknownLabel`. */
export function documentsByField(
  docs: readonly DocumentSummary[],
  pattern: RegExp,
  unknownLabel = 'Unknown',
): readonly LabeledCount[] {
  const counts = new Map<string, number>()
  for (const doc of docs) {
    const label = fieldValue(doc, pattern) ?? unknownLabel
    counts.set(label, (counts.get(label) ?? 0) + 1)
  }
  return [...counts.entries()]
    .map(([label, count]) => ({ label, count }))
    .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label))
}

export function documentsByState(docs: readonly DocumentSummary[]): readonly LabeledCount[] {
  return documentsByField(docs, STATE_RE)
}

export function documentsByCounty(docs: readonly DocumentSummary[]): readonly LabeledCount[] {
  return documentsByField(docs, COUNTY_RE)
}

export function ingestByDay(docs: readonly DocumentSummary[]): readonly DayCount[] {
  const counts = new Map<string, number>()
  for (const doc of docs) {
    const ingested = new Date(doc.ingestedAt)
    if (Number.isNaN(ingested.getTime())) {
      continue
    }
    const day = ingested.toISOString().slice(0, 10)
    counts.set(day, (counts.get(day) ?? 0) + 1)
  }
  return [...counts.entries()]
    .map(([date, count]) => ({ date, count }))
    .sort((a, b) => a.date.localeCompare(b.date))
}

/** Parse a document's lease term/expiration date from a date-like field, or null if none parses. */
export function findExpirationDate(doc: DocumentSummary): Date | null {
  for (const field of doc.fields) {
    if (EXPIRATION_RE.test(field.name)) {
      const parsed = new Date(field.value)
      if (!Number.isNaN(parsed.getTime())) {
        return parsed
      }
    }
  }
  return null
}

function bucketFor(daysUntil: number): ExpirationBucket {
  if (daysUntil < 0) {
    return 'expired'
  }
  if (daysUntil <= 30) {
    return 'within30'
  }
  if (daysUntil <= 60) {
    return 'within60'
  }
  if (daysUntil <= 90) {
    return 'within90'
  }
  return 'later'
}

export function upcomingExpirations(
  docs: readonly DocumentSummary[],
  now: Date = new Date(),
): readonly UpcomingExpiration[] {
  const result: UpcomingExpiration[] = []
  for (const doc of docs) {
    const date = findExpirationDate(doc)
    if (date === null) {
      continue
    }
    const daysUntil = Math.ceil((date.getTime() - now.getTime()) / DAY_MS)
    result.push({ document: doc, date, daysUntil, bucket: bucketFor(daysUntil) })
  }
  return result.sort((a, b) => a.date.getTime() - b.date.getTime())
}

export function expirationBucketCounts(
  docs: readonly DocumentSummary[],
  now: Date = new Date(),
): Record<ExpirationBucket, number> {
  const counts: Record<ExpirationBucket, number> = {
    expired: 0,
    within30: 0,
    within60: 0,
    within90: 0,
    later: 0,
  }
  for (const expiration of upcomingExpirations(docs, now)) {
    counts[expiration.bucket] += 1
  }
  return counts
}
