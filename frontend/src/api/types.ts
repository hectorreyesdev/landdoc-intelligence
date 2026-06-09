// DTOs mirroring the committed backend contracts (specs 0001 + 0002).
// These are the wire shapes; only the typed client (./client) reads/writes them.

/** An extracted structured field from `POST /documents` (spec 0001). */
export interface ExtractedField {
  readonly name: string
  readonly value: string
  /** May be null when a field isn't pinned to a source chunk. */
  readonly sourceChunkId: string | null
}

/** `201` body from `POST /documents` (spec 0001). */
export interface DocumentResponse {
  readonly id: string
  readonly fileName: string
  readonly status: string
  readonly fields: readonly ExtractedField[]
  readonly chunkCount: number
}

/** A citation in an `/ask` answer (spec 0002, amended by 0006): resolves a claim to its source chunk. */
export interface Citation {
  readonly chunkId: string
  readonly documentId: string
  readonly score: number
  readonly text: string
  /** The source document's file name — used to label the citation and link to its viewer (ADR-0014). */
  readonly source: string
}

/** `200` body from `POST /ask` (spec 0002). */
export interface AskResponse {
  readonly answer: string
  readonly citations: readonly Citation[]
}

/**
 * A persisted document's metadata + extracted fields (spec 0006). Returned by `GET /documents` (list)
 * and `GET /documents/{id}` (detail). The original file is fetched separately via its URL.
 */
export interface DocumentSummary {
  readonly id: string
  readonly fileName: string
  readonly status: string
  readonly contentType: string
  readonly chunkCount: number
  readonly fields: readonly ExtractedField[]
  readonly ingestedAt: string
}

/** The time window for the Ops / Usage dashboard (spec 0009); the `range` query value for `GET /usage`. */
export type UsageRange = '24h' | '7d' | '30d'

/** Token totals for a window, with the server-computed estimated cost (spec 0009). */
export interface UsageTotals {
  readonly promptTokens: number
  readonly completionTokens: number
  readonly totalTokens: number
  readonly estimatedCostUsd: number
}

/** Per-deployment token usage + estimated cost (spec 0009). */
export interface DeploymentUsage {
  readonly deployment: string
  readonly promptTokens: number
  readonly completionTokens: number
  readonly totalTokens: number
  readonly estimatedCostUsd: number
}

/** Request volume + health for a window (spec 0009): success 2xx · clientErrors 4xx · throttled429 · serverErrors 5xx. */
export interface UsageRequests {
  readonly total: number
  readonly success: number
  readonly clientErrors: number
  readonly throttled429: number
  readonly serverErrors: number
}

/** Response latency for a window, in milliseconds (spec 0009). */
export interface UsageLatency {
  readonly avgMs: number
  readonly maxMs: number
}

/** `200` body from `GET /usage` (spec 0009): LLM usage + estimated cost, read live from Azure Monitor metrics. */
export interface UsageReport {
  readonly range: string
  readonly from: string
  readonly to: string
  readonly totals: UsageTotals
  readonly deployments: readonly DeploymentUsage[]
  readonly requests: UsageRequests
  readonly latency: UsageLatency
}
