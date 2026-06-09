import { afterEach, describe, expect, it, vi } from 'vitest'
import { ask, deleteDocument, documentFileUrl, getDocument, listDocuments, uploadDocument } from './client'
import type { AskResponse, DocumentResponse, DocumentSummary } from './types'

interface FakeOpts {
  reject?: boolean
  badBody?: boolean
}

function stubFetch(status: number, body: unknown, opts: FakeOpts = {}): ReturnType<typeof vi.fn> {
  const fn = vi.fn(async () => {
    if (opts.reject) throw new TypeError('network down')
    return {
      ok: status >= 200 && status < 300,
      status,
      json: async () => {
        if (opts.badBody) throw new SyntaxError('not json')
        return body
      },
    } as unknown as Response
  })
  vi.stubGlobal('fetch', fn)
  return fn
}

afterEach(() => vi.unstubAllGlobals())

const docBody: DocumentResponse = {
  id: 'doc-1',
  fileName: 'synthetic-lease-01.pdf',
  status: 'ready',
  fields: [{ name: 'Lessor', value: 'Jane Roe', sourceChunkId: 'c1' }],
  chunkCount: 7,
}

const askBody: AskResponse = {
  answer: 'The lessee is Acme Minerals LLC.',
  citations: [
    { chunkId: 'c1', documentId: 'doc-1', score: 0.82, text: '…as Lessee…', source: 'synthetic-lease-01.pdf' },
  ],
}

const summary: DocumentSummary = {
  id: 'doc-1',
  fileName: 'synthetic-lease-01.pdf',
  status: 'ready',
  contentType: 'application/pdf',
  chunkCount: 7,
  fields: [{ name: 'Lessor', value: 'Jane Roe', sourceChunkId: 'c1' }],
  ingestedAt: '2026-06-08T12:00:00+00:00',
}

function pdf(): File {
  return new File(['%PDF-1.4'], 'synthetic-lease-01.pdf', { type: 'application/pdf' })
}

describe('uploadDocument', () => {
  it('returns the typed DTO on 201 and posts FormData without a manual Content-Type', async () => {
    const fetchMock = stubFetch(201, docBody)

    const result = await uploadDocument(pdf())

    expect(result).toEqual({ ok: true, value: docBody })
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/documents')
    expect(init.method).toBe('POST')
    expect(init.body).toBeInstanceOf(FormData)
    expect((init.body as FormData).get('file')).toBeInstanceOf(File)
    expect(init.headers).toBeUndefined() // browser sets the multipart boundary
  })

  it('maps 400 to a validation error', async () => {
    stubFetch(400, { detail: 'File must be a PDF.' })
    const result = await uploadDocument(pdf())
    expect(result).toEqual({ ok: false, error: { kind: 'validation', status: 400, detail: 'File must be a PDF.' } })
  })
})

describe('ask', () => {
  it('returns the typed DTO on 200 and posts JSON to the relative path', async () => {
    const fetchMock = stubFetch(200, askBody)

    const result = await ask('Who is the lessee?')

    expect(result).toEqual({ ok: true, value: askBody })
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/ask')
    expect(init.method).toBe('POST')
    expect(init.headers).toEqual({ 'Content-Type': 'application/json' })
    expect(init.body).toBe(JSON.stringify({ question: 'Who is the lessee?' }))
  })

  it('maps 400 → validation', async () => {
    stubFetch(400, { title: 'Bad Request' })
    const result = await ask('')
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.error.kind).toBe('validation')
      expect(result.error.status).toBe(400)
      expect(result.error.detail).toBe('Bad Request')
    }
  })

  it('maps 409 → empty-store', async () => {
    stubFetch(409, { detail: 'No documents ingested.' })
    const result = await ask('Who is the lessee?')
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error.kind).toBe('empty-store')
  })

  it('maps 501 → not-implemented (defensive)', async () => {
    stubFetch(501, {})
    const result = await ask('Who is the lessee?')
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error.kind).toBe('not-implemented')
  })

  it('maps any other non-OK status → server', async () => {
    stubFetch(503, { detail: 'upstream down' })
    const result = await ask('Who is the lessee?')
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.error.kind).toBe('server')
      expect(result.error.status).toBe(503)
    }
  })

  it('maps a thrown fetch → network error', async () => {
    stubFetch(0, null, { reject: true })
    const result = await ask('Who is the lessee?')
    expect(result).toEqual({ ok: false, error: { kind: 'network', status: null, detail: null } })
  })

  it('tolerates a missing/garbage ProblemDetails body (no throw, null detail)', async () => {
    stubFetch(400, null, { badBody: true })
    const result = await ask('')
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.error.kind).toBe('validation')
      expect(result.error.detail).toBeNull()
    }
  })
})

describe('listDocuments', () => {
  it('returns the typed array on 200 from the relative path', async () => {
    const fetchMock = stubFetch(200, [summary])

    const result = await listDocuments()

    expect(result).toEqual({ ok: true, value: [summary] })
    const [url] = fetchMock.mock.calls[0] as [string]
    expect(url).toBe('/documents')
  })

  it('maps any non-OK status → server', async () => {
    stubFetch(503, { detail: 'down' })
    const result = await listDocuments()
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error.kind).toBe('server')
  })

  it('maps a thrown fetch → network error', async () => {
    stubFetch(0, null, { reject: true })
    const result = await listDocuments()
    expect(result).toEqual({ ok: false, error: { kind: 'network', status: null, detail: null } })
  })
})

describe('getDocument', () => {
  it('returns the typed DTO on 200', async () => {
    const fetchMock = stubFetch(200, summary)

    const result = await getDocument('doc-1')

    expect(result).toEqual({ ok: true, value: summary })
    const [url] = fetchMock.mock.calls[0] as [string]
    expect(url).toBe('/documents/doc-1')
  })

  it('maps 404 → server', async () => {
    stubFetch(404, { title: 'Document not found.' })
    const result = await getDocument('missing')
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.error.kind).toBe('server')
      expect(result.error.status).toBe(404)
    }
  })
})

describe('deleteDocument', () => {
  it('returns ok on 204 with a DELETE to the relative path', async () => {
    const fetchMock = stubFetch(204, null)

    const result = await deleteDocument('doc-1')

    expect(result).toEqual({ ok: true, value: undefined })
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/documents/doc-1')
    expect(init.method).toBe('DELETE')
  })

  it('maps a non-OK status → server', async () => {
    stubFetch(500, { detail: 'boom' })
    const result = await deleteDocument('doc-1')
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error.kind).toBe('server')
  })

  it('maps a thrown fetch → network error', async () => {
    stubFetch(0, null, { reject: true })
    const result = await deleteDocument('doc-1')
    expect(result).toEqual({ ok: false, error: { kind: 'network', status: null, detail: null } })
  })
})

describe('documentFileUrl', () => {
  it('builds the same-origin relative file URL', () => {
    expect(documentFileUrl('doc-1')).toBe('/documents/doc-1/file')
  })
})
