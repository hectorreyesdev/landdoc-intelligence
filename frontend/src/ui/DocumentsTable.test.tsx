import { expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DocumentsTable } from './DocumentsTable'
import type { DocumentSummary } from '../api/types'

const docs: readonly DocumentSummary[] = [
  {
    id: 'd1',
    fileName: 'lease-a.pdf',
    status: 'ready',
    contentType: 'application/pdf',
    chunkCount: 4,
    fields: [{ name: 'Lessee', value: 'Acme Minerals LLC', sourceChunkId: null }],
    ingestedAt: '2026-06-08T12:00:00+00:00',
  },
  {
    id: 'd2',
    fileName: 'lease-b.md',
    status: 'ready',
    contentType: 'text/markdown',
    chunkCount: 2,
    fields: [],
    ingestedAt: '2026-06-08T13:00:00+00:00',
  },
]

it('renders a row per document with file name, chunk count, and fields', () => {
  render(<DocumentsTable documents={docs} onOpenDocument={() => {}} />)

  expect(screen.getByText('lease-a.pdf')).toBeInTheDocument()
  expect(screen.getByText('lease-b.md')).toBeInTheDocument()
  expect(screen.getByText(/Acme Minerals LLC/)).toBeInTheDocument()
  // Two "View" buttons — one per row.
  expect(screen.getAllByRole('button', { name: /view/i })).toHaveLength(2)
})

it('clicking View calls onOpenDocument with the row id', async () => {
  const onOpenDocument = vi.fn()
  render(<DocumentsTable documents={docs} onOpenDocument={onOpenDocument} />)

  await userEvent.click(screen.getAllByRole('button', { name: /view/i })[0])

  expect(onOpenDocument).toHaveBeenCalledWith('d1')
})

it('shows an empty state when there are no documents', () => {
  render(<DocumentsTable documents={[]} onOpenDocument={() => {}} />)

  expect(screen.getByText(/no documents ingested yet/i)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /view/i })).not.toBeInTheDocument()
})
