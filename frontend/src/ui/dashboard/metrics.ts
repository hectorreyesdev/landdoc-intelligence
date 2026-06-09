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

export interface HourCount {
  readonly hour: string // YYYY-MM-DDTHH:00 (UTC)
  readonly count: number
}

export interface StateCountyCount {
  readonly state: string
  readonly county: string
  readonly count: number
  /** Ids of the documents in this county — lets the map open one on click. */
  readonly documentIds: readonly string[]
}

export type ExpirationBucket = 'expired' | 'within30' | 'within60' | 'within90' | 'later'

export interface UpcomingExpiration {
  readonly document: DocumentSummary
  readonly date: Date
  readonly daysUntil: number
  readonly bucket: ExpirationBucket
  /** `derived` = computed from effective date + primary term; `explicit` = read from a date field. */
  readonly basis: TermEndBasis
  /** The lease's start (effective date) when the term end was derived; null for an explicit date field. */
  readonly start: Date | null
}

export type TermEndBasis = 'explicit' | 'derived'

export interface TermEnd {
  readonly date: Date
  readonly basis: TermEndBasis
  /** Effective date the term end was computed from (derived basis); null for an explicit field. */
  readonly start: Date | null
}

const PARTY_RE = /lessee|lessor|grantor|grantee|party|buyer|seller/i
const DATE_RE = /date/i
export const COUNTY_RE = /county/i
export const STATE_RE = /\bstate\b/i
const EXPIRATION_RE = /expir|term end|end date/i
const EFFECTIVE_RE = /effective/i
const PRIMARY_TERM_RE = /primary\s*term|\bterm\b/i
const DAY_MS = 86_400_000

const NUMBER_WORDS: Record<string, number> = {
  one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10,
  eleven: 11, twelve: 12, fifteen: 15, twenty: 20, twentyfive: 25, thirty: 30,
}

/** First non-empty value of a field whose name matches the pattern, or null. */
export function fieldValue(doc: DocumentSummary, pattern: RegExp): string | null {
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

export function ingestByHour(docs: readonly DocumentSummary[]): readonly HourCount[] {
  const counts = new Map<string, number>()
  for (const doc of docs) {
    const ingested = new Date(doc.ingestedAt)
    if (Number.isNaN(ingested.getTime())) {
      continue
    }
    // YYYY-MM-DDTHH from the ISO string, pinned to ":00" — UTC keeps bucketing deterministic across machines.
    const hour = `${ingested.toISOString().slice(0, 13)}:00`
    counts.set(hour, (counts.get(hour) ?? 0) + 1)
  }
  return [...counts.entries()]
    .map(([hour, count]) => ({ hour, count }))
    .sort((a, b) => a.hour.localeCompare(b.hour))
}

/** Count documents grouped by their (state, county) pair; documents missing either field are skipped. */
export function documentsByStateCounty(docs: readonly DocumentSummary[]): readonly StateCountyCount[] {
  const counts = new Map<string, StateCountyCount>()
  for (const doc of docs) {
    const state = fieldValue(doc, STATE_RE)
    const county = fieldValue(doc, COUNTY_RE)
    if (state === null || county === null) {
      continue
    }
    const key = `${state.toLowerCase()}|${county.toLowerCase()}`
    const existing = counts.get(key)
    counts.set(key, {
      state,
      county,
      count: (existing?.count ?? 0) + 1,
      documentIds: [...(existing?.documentIds ?? []), doc.id],
    })
  }
  return [...counts.values()].sort((a, b) => b.count - a.count || a.state.localeCompare(b.state))
}

/**
 * Parse a primary-term length ("five (5) years", "three (3) years from the effective date", "36 months")
 * into years + months, or null if no number/unit is found. Prefers a parenthetical digit, then any digit
 * run, then a spelled-out number word.
 */
export function parseTermDuration(value: string): { years: number; months: number } | null {
  const text = value.toLowerCase()
  const unit = /month/.test(text) ? 'months' : /year|annum|\byrs?\b/.test(text) ? 'years' : null
  if (unit === null) {
    return null
  }
  let n: number | null = null
  const paren = text.match(/\((\d+)\)/)
  const digits = text.match(/\d+/)
  const word = text.match(/\b(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty)\b/)
  if (paren !== null) {
    n = Number(paren[1])
  } else if (digits !== null) {
    n = Number(digits[0])
  } else if (word !== null) {
    n = NUMBER_WORDS[word[1]] ?? null
  }
  if (n === null || !Number.isFinite(n) || n <= 0) {
    return null
  }
  return unit === 'months' ? { years: 0, months: n } : { years: n, months: 0 }
}

/**
 * Parse a date string as a **local calendar date**. ISO `YYYY-MM-DD` is forced to local midnight (JS would
 * otherwise read it as UTC, shifting the day in non-UTC zones); other forms (e.g. "May 5, 2024") already
 * parse local. Returns null if unparseable. Keeps parse → add → format all in one timezone.
 */
export function parseLocalDate(value: string): Date | null {
  const iso = value.trim().match(/^(\d{4})-(\d{2})-(\d{2})$/)
  const date = iso
    ? new Date(Number(iso[1]), Number(iso[2]) - 1, Number(iso[3]))
    : new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

/** Add a year/month duration to a date, returning a new Date (never mutates the input). */
function addDuration(date: Date, duration: { years: number; months: number }): Date {
  const out = new Date(date.getTime())
  out.setFullYear(out.getFullYear() + duration.years)
  out.setMonth(out.getMonth() + duration.months)
  return out
}

/**
 * A document's primary-term end date. Prefers an explicit expiration/term-end **date field** if one exists
 * (future-proof); otherwise computes it from **effective date + primary term** — which is how oil & gas
 * leases actually define their term (they carry no explicit expiration date). Null when neither is derivable.
 */
export function findTermEnd(doc: DocumentSummary): TermEnd | null {
  for (const field of doc.fields) {
    if (EXPIRATION_RE.test(field.name)) {
      const parsed = parseLocalDate(field.value)
      if (parsed !== null) {
        return { date: parsed, basis: 'explicit', start: null }
      }
    }
  }
  const effectiveRaw = fieldValue(doc, EFFECTIVE_RE)
  const termRaw = fieldValue(doc, PRIMARY_TERM_RE)
  if (effectiveRaw !== null && termRaw !== null) {
    const effective = parseLocalDate(effectiveRaw)
    const duration = parseTermDuration(termRaw)
    if (effective !== null && duration !== null) {
      return { date: addDuration(effective, duration), basis: 'derived', start: effective }
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
    const termEnd = findTermEnd(doc)
    if (termEnd === null) {
      continue
    }
    const daysUntil = Math.ceil((termEnd.date.getTime() - now.getTime()) / DAY_MS)
    result.push({
      document: doc,
      date: termEnd.date,
      daysUntil,
      bucket: bucketFor(daysUntil),
      basis: termEnd.basis,
      start: termEnd.start,
    })
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
