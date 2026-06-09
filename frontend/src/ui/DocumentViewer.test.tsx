import { beforeEach, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DocumentViewer } from './DocumentViewer'
import * as client from '../api/client'
import type { DocumentSummary } from '../api/types'

vi.mock('../api/client')

beforeEach(() => {
  vi.resetAllMocks()
  // documentFileUrl is a pure helper; restore its real behaviour for the iframe src assertion.
  vi.mocked(client.documentFileUrl).mockImplementation((id: string) => `/documents/${id}/file`)
})

const summary: DocumentSummary = {
  id: 'd1',
  fileName: 'lease-a.pdf',
  status: 'ready',
  contentType: 'application/pdf',
  chunkCount: 4,
  fields: [{ name: 'Lessee', value: 'Acme Minerals LLC', sourceChunkId: null }],
  ingestedAt: '2026-06-08T12:00:00+00:00',
}

it('loads the document and embeds the original file in an iframe', async () => {
  vi.mocked(client.getDocument).mockResolvedValue({ ok: true, value: summary })

  render(<DocumentViewer documentId="d1" onClose={() => {}} />)

  // Fields render…
  expect(await screen.findByText('Acme Minerals LLC')).toBeInTheDocument()
  // …and the original file is embedded via its same-origin URL.
  const frame = screen.getByTitle(/source file: lease-a\.pdf/i)
  expect(frame).toHaveAttribute('src', '/documents/d1/file')
})

it('shows an error state when the document cannot be loaded', async () => {
  vi.mocked(client.getDocument).mockResolvedValue({
    ok: false,
    error: { kind: 'server', status: 404, detail: null },
  })

  render(<DocumentViewer documentId="missing" onClose={() => {}} />)

  expect(await screen.findByRole('alert')).toHaveTextContent(/could not load/i)
})

it('closes on the × button and on Escape', async () => {
  vi.mocked(client.getDocument).mockResolvedValue({ ok: true, value: summary })
  const onClose = vi.fn()

  render(<DocumentViewer documentId="d1" onClose={onClose} />)
  await screen.findByText('Acme Minerals LLC')

  await userEvent.click(screen.getByRole('button', { name: /close/i }))
  expect(onClose).toHaveBeenCalledTimes(1)

  await userEvent.keyboard('{Escape}')
  expect(onClose).toHaveBeenCalledTimes(2)
})
