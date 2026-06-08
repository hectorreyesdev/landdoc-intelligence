import { beforeEach, expect, it, vi } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { useDocuments } from './useDocuments'
import * as client from '../api/client'
import type { ApiResult } from '../api/client'
import type { DocumentResponse } from '../api/types'

vi.mock('../api/client')

beforeEach(() => {
  vi.resetAllMocks()
})

function doc(overrides: Partial<DocumentResponse> = {}): DocumentResponse {
  return {
    id: 'd1',
    fileName: 'lease.pdf',
    status: 'ready',
    fields: [{ name: 'Lessee', value: 'Acme Minerals LLC', sourceChunkId: null }],
    chunkCount: 3,
    ...overrides,
  }
}

function pdf(name: string): File {
  return new File(['%PDF-1.4'], name, { type: 'application/pdf' })
}

function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((r) => {
    resolve = r
  })
  return { promise, resolve }
}

it('shows a pending tile up front, then solidifies it to ready in place', async () => {
  const pending = deferred<ApiResult<DocumentResponse>>()
  vi.mocked(client.uploadDocument).mockReturnValue(pending.promise)
  const { result } = renderHook(() => useDocuments())

  act(() => {
    void result.current.ingest([pdf('a.pdf')])
  })

  // Tile appears immediately as uploading, with batch progress started and ask not yet enabled.
  await waitFor(() => expect(result.current.items).toHaveLength(1))
  expect(result.current.items[0].status).toBe('uploading')
  expect(result.current.items[0].fileName).toBe('a.pdf')
  expect(result.current.progress).toEqual({ done: 0, total: 1 })
  expect(result.current.hasReady).toBe(false)

  await act(async () => {
    pending.resolve({ ok: true, value: doc({ fileName: 'a.pdf' }) })
  })

  // Same tile (still one) is now ready; progress clears; ask enabled.
  await waitFor(() => expect(result.current.items[0].status).toBe('ready'))
  expect(result.current.items).toHaveLength(1)
  expect(result.current.hasReady).toBe(true)
  expect(result.current.progress).toBeNull()
})

it('marks a failed file as an error tile but still ingests the rest', async () => {
  vi.mocked(client.uploadDocument)
    .mockResolvedValueOnce({ ok: true, value: doc({ fileName: 'a.pdf' }) })
    .mockResolvedValueOnce({ ok: false, error: { kind: 'server', status: 500, detail: null } })
  const { result } = renderHook(() => useDocuments())

  await act(async () => {
    await result.current.ingest([pdf('a.pdf'), pdf('bad.pdf')])
  })

  expect(result.current.items.map((item) => item.status)).toEqual(['ready', 'error'])
  expect(result.current.hasReady).toBe(true)
  expect(client.uploadDocument).toHaveBeenCalledTimes(2)
})
