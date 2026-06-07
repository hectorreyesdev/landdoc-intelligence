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

// The acceptance beat: the upload/fields path must never depend on the ask path. Even when
// /ask degrades (here: 501), ingest still works and the fields stay on screen.
it('keeps upload + fields working when /ask is unavailable (501)', async () => {
  vi.mocked(client.uploadDocument).mockResolvedValue({ ok: true, value: doc })
  vi.mocked(client.ask).mockResolvedValue({
    ok: false,
    error: { kind: 'not-implemented', status: 501, detail: null },
  })
  render(<App />)

  await userEvent.upload(
    screen.getByLabelText(/pdf file/i),
    new File(['%PDF-1.4'], 'lease.pdf', { type: 'application/pdf' }),
  )
  await userEvent.click(screen.getByRole('button', { name: /upload/i }))
  expect(await screen.findByText('lease.pdf')).toBeInTheDocument()
  expect(screen.getByText('Acme Minerals LLC')).toBeInTheDocument()

  await userEvent.type(screen.getByRole('textbox', { name: /question/i }), 'Who is the lessee?')
  await userEvent.click(screen.getByRole('button', { name: /^ask$/i }))

  // ask half degraded gracefully…
  expect(await screen.findByText(/not available/i)).toBeInTheDocument()
  // …and the upload/fields half is untouched.
  expect(screen.getByText('lease.pdf')).toBeInTheDocument()
  expect(screen.getByText('Acme Minerals LLC')).toBeInTheDocument()
})
