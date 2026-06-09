import { afterEach, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DocumentsTable } from './DocumentsTable'
import type { DocumentSummary } from '../api/types'

afterEach(() => vi.unstubAllGlobals())

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

it('filters rows by the search query (file name or fields)', async () => {
  render(<DocumentsTable documents={docs} onOpenDocument={() => {}} />)

  await userEvent.type(screen.getByRole('searchbox', { name: /search documents/i }), 'lease-b')

  expect(screen.queryByText('lease-a.pdf')).not.toBeInTheDocument()
  expect(screen.getByText('lease-b.md')).toBeInTheDocument()
})

it('exports the shown documents to CSV', async () => {
  const createObjectURL = vi.fn(() => 'blob:fake')
  const revokeObjectURL = vi.fn()
  vi.stubGlobal('URL', { createObjectURL, revokeObjectURL })
  const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

  render(<DocumentsTable documents={docs} onOpenDocument={() => {}} />)
  await userEvent.click(screen.getByRole('button', { name: /export csv/i }))

  expect(createObjectURL).toHaveBeenCalledOnce()
  expect(clickSpy).toHaveBeenCalledOnce()

  clickSpy.mockRestore()
})
