// The single typed API client — the app's only port to the backend, and the ONLY module
// that calls fetch (CLAUDE.md TS conventions; ADR-0006). Components/hooks call these typed
// methods; they never touch fetch directly.
//
// Transport is single-origin (ADR-0011): every request uses a RELATIVE path. In dev the Vite
// proxy forwards /documents and /ask to the backend; in prod a single container serves the SPA
// and the API on one origin (same-origin, no CORS). There is no base URL here — by design.

import type { AskResponse, DocumentResponse, DocumentSummary, UsageRange, UsageReport } from './types'

/** Why a request didn't succeed, keyed off HTTP status (the UI renders a state per kind). */
export type ApiErrorKind =
  | 'validation' // 400 — bad input (blank question; missing/empty/non-PDF file)
  | 'empty-store' // 409 — /ask against a store with nothing ingested
  | 'not-implemented' // 501 — endpoint not built yet (defensive; /ask is live today)
  | 'server' // any other non-OK status (5xx, 404, …)
  | 'network' // fetch itself threw (offline / DNS / connection refused)

export interface ApiError {
  readonly kind: ApiErrorKind
  /** HTTP status, or null for a network-level failure (no response). */
  readonly status: number | null
  /** ProblemDetails `detail`/`title` when present and parseable; null otherwise. */
  readonly detail: string | null
}

/** Discriminated result — the client returns failures, it does not throw them at callers. */
export type ApiResult<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly error: ApiError }

/**
 * Best-effort read of an RFC 7807 ProblemDetails body. The client must tolerate a missing,
 * empty, or non-JSON body (a bare status with no payload), so any failure yields null.
 */
async function readProblemDetail(response: Response): Promise<string | null> {
  try {
    const body: unknown = await response.json()
    if (body !== null && typeof body === 'object') {
      const record = body as Record<string, unknown>
      const detail = record.detail ?? record.title
      return typeof detail === 'string' ? detail : null
    }
    return null
  } catch {
    return null
  }
}

function errorForStatus(status: number, detail: string | null): ApiError {
  switch (status) {
    case 400:
      return { kind: 'validation', status, detail }
    case 409:
      return { kind: 'empty-store', status, detail }
    case 501:
      return { kind: 'not-implemented', status, detail }
    default:
      return { kind: 'server', status, detail }
  }
}

const NETWORK_ERROR: ApiError = { kind: 'network', status: null, detail: null }

/**
 * Upload one PDF and ingest it (spec 0001). The body is multipart/form-data with a single
 * `file` part; we deliberately do NOT set Content-Type so the browser adds the multipart
 * boundary itself.
 */
export async function uploadDocument(file: File): Promise<ApiResult<DocumentResponse>> {
  const form = new FormData()
  form.append('file', file)

  let response: Response
  try {
    response = await fetch('/documents', { method: 'POST', body: form })
  } catch {
    return { ok: false, error: NETWORK_ERROR }
  }

  if (response.ok) {
    return { ok: true, value: (await response.json()) as DocumentResponse }
  }
  return { ok: false, error: errorForStatus(response.status, await readProblemDetail(response)) }
}

/**
 * Ask a question grounded in the ingested corpus (spec 0002). Returns a typed error for
 * 400 (blank question), 409 (empty store), 501 (defensive), any other non-OK status, or a
 * network failure — never an unhandled rejection.
 */
export async function ask(question: string): Promise<ApiResult<AskResponse>> {
  let response: Response
  try {
    response = await fetch('/ask', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question }),
    })
  } catch {
    return { ok: false, error: NETWORK_ERROR }
  }

  if (response.ok) {
    return { ok: true, value: (await response.json()) as AskResponse }
  }
  return { ok: false, error: errorForStatus(response.status, await readProblemDetail(response)) }
}

/** List every ingested document's metadata + fields (spec 0006). Empty corpus → an empty array. */
export async function listDocuments(): Promise<ApiResult<DocumentSummary[]>> {
  let response: Response
  try {
    response = await fetch('/documents')
  } catch {
    return { ok: false, error: NETWORK_ERROR }
  }

  if (response.ok) {
    return { ok: true, value: (await response.json()) as DocumentSummary[] }
  }
  return { ok: false, error: errorForStatus(response.status, await readProblemDetail(response)) }
}

/** Fetch one document's metadata + fields (spec 0006). A 404 maps to a `server` error kind. */
export async function getDocument(id: string): Promise<ApiResult<DocumentSummary>> {
  let response: Response
  try {
    response = await fetch(`/documents/${encodeURIComponent(id)}`)
  } catch {
    return { ok: false, error: NETWORK_ERROR }
  }

  if (response.ok) {
    return { ok: true, value: (await response.json()) as DocumentSummary }
  }
  return { ok: false, error: errorForStatus(response.status, await readProblemDetail(response)) }
}

/** Delete a document — its file + metadata and all its chunks (spec 0008). Idempotent; 204 on success. */
export async function deleteDocument(id: string): Promise<ApiResult<void>> {
  let response: Response
  try {
    response = await fetch(`/documents/${encodeURIComponent(id)}`, { method: 'DELETE' })
  } catch {
    return { ok: false, error: NETWORK_ERROR }
  }

  if (response.ok) {
    return { ok: true, value: undefined }
  }
  return { ok: false, error: errorForStatus(response.status, await readProblemDetail(response)) }
}

/**
 * LLM usage + estimated cost for a time range (spec 0009). The backend reads live from Azure Monitor
 * platform metrics each call; cost is a computed estimate. Maps status → ApiResult like the others
 * (an invalid range is a 400 → `validation`).
 */
export async function getUsage(range: UsageRange): Promise<ApiResult<UsageReport>> {
  let response: Response
  try {
    response = await fetch(`/usage?range=${encodeURIComponent(range)}`)
  } catch {
    return { ok: false, error: NETWORK_ERROR }
  }

  if (response.ok) {
    return { ok: true, value: (await response.json()) as UsageReport }
  }
  return { ok: false, error: errorForStatus(response.status, await readProblemDetail(response)) }
}

/**
 * Same-origin URL for a document's original file (spec 0006). Returned as a string so callers embed it
 * directly in an `<iframe>`/`<object>` — the bytes never pass through fetch/ApiResult, which keeps the
 * single-typed-client invariant (only this module calls fetch) intact.
 */
export function documentFileUrl(id: string): string {
  return `/documents/${encodeURIComponent(id)}/file`
}

/**
 * Fetch a document's original file as text (spec 0006 amendment — formatted markdown rendering in the
 * viewer). Used only for text-based formats (`text/markdown`, `text/plain`) so the viewer can render the
 * content itself instead of embedding raw bytes in an `<iframe>`. PDFs still use {@link documentFileUrl}.
 * Routed through the typed client so the single-fetch invariant holds.
 */
export async function getDocumentFileText(id: string): Promise<ApiResult<string>> {
  let response: Response
  try {
    response = await fetch(`/documents/${encodeURIComponent(id)}/file`)
  } catch {
    return { ok: false, error: NETWORK_ERROR }
  }

  if (response.ok) {
    return { ok: true, value: await response.text() }
  }
  return { ok: false, error: errorForStatus(response.status, await readProblemDetail(response)) }
}
