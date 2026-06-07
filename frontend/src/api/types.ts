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

/** A citation in an `/ask` answer (spec 0002): resolves a claim to its source chunk. */
export interface Citation {
  readonly chunkId: string
  readonly documentId: string
  readonly score: number
  readonly text: string
}

/** `200` body from `POST /ask` (spec 0002). */
export interface AskResponse {
  readonly answer: string
  readonly citations: readonly Citation[]
}
