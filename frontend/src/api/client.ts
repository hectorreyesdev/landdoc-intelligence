// The single typed API client — the app's only port to the backend, and the ONLY module
// that calls fetch (CLAUDE.md TS conventions; ADR-0006). Components/hooks call these typed
// methods; they never touch fetch directly.
//
// Transport is single-origin (ADR-0011): every request uses a RELATIVE path. In dev the Vite
// proxy forwards /documents and /ask to the backend; in prod a single container serves the SPA
// and the API on one origin (same-origin, no CORS). There is no base URL here — by design.

import type { AskResponse, DocumentResponse } from './types'

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
