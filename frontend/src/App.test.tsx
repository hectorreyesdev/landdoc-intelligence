import { beforeEach, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { App } from './App'
import * as client from './api/client'
import type { DocumentResponse } from './api/types'

vi.mock('./api/client')

beforeEach(() => {
  vi.resetAllMocks()
})

const doc: DocumentResponse = {
  id: 'd1',
  fileName: 'lease.pdf',
  status: 'ready',
  fields: [{ name: 'Lessee', value: 'Acme Minerals LLC', sourceChunkId: null }],
  chunkCount: 3,
}

// The acceptance beat: the upload/documents path must never depend on the ask path. Even when /ask
// degrades (here: 501), ingest still works and the document tile stays on screen.
it('keeps upload + the document list working when /ask is unavailable (501)', async () => {
  vi.mocked(client.uploadDocument).mockResolvedValue({ ok: true, value: doc })
  vi.mocked(client.ask).mockResolvedValue({
    ok: false,
    error: { kind: 'not-implemented', status: 501, detail: null },
  })
  // The persisted documents table loads on mount and after each upload; keep it empty here so the
  // session card grid is the only place the uploaded document appears (unambiguous assertions below).
  vi.mocked(client.listDocuments).mockResolvedValue({ ok: true, value: [] })
  render(<App />)

  await userEvent.upload(
    screen.getByLabelText(/document files/i),
    new File(['%PDF-1.4'], 'lease.pdf', { type: 'application/pdf' }),
  )
  // The tile solidifies to ready with its extracted field shown.
  expect(await screen.findByText('Acme Minerals LLC')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'lease.pdf' })).toBeInTheDocument()

  await userEvent.type(screen.getByRole('textbox', { name: /question/i }), 'Who is the lessee?')
  await userEvent.click(screen.getByRole('button', { name: /^ask$/i }))

  // ask half degraded gracefully…
  expect(await screen.findByText(/not available/i)).toBeInTheDocument()
  // …and the document list is untouched.
  expect(screen.getByRole('heading', { name: 'lease.pdf' })).toBeInTheDocument()
  expect(screen.getByText('Acme Minerals LLC')).toBeInTheDocument()
})
