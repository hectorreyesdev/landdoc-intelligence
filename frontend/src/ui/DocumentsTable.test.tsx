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

it('renders a row per document with file name, chunk count, and a field count', () => {
  render(<DocumentsTable documents={docs} onOpenDocument={() => {}} />)

  expect(screen.getByText('lease-a.pdf')).toBeInTheDocument()
  expect(screen.getByText('lease-b.md')).toBeInTheDocument()
  // Fields are summarized as a count in the table; the full set lives in the viewer.
  expect(screen.getByText('1 field')).toBeInTheDocument()
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

it('sorts rows when a column header is clicked and reflects direction via aria-sort', async () => {
  render(<DocumentsTable documents={docs} onOpenDocument={() => {}} />)

  function fileCellOrder(): string[] {
    return screen.getAllByRole('cell').map((c) => c.textContent ?? '').filter((t) => /\.(pdf|md)$/.test(t))
  }

  // Default (server) order: lease-a before lease-b.
  expect(fileCellOrder()).toEqual(['lease-a.pdf', 'lease-b.md'])

  // Click Chunks: ascending → lease-b (2) before lease-a (4).
  const chunksHeader = screen.getByRole('button', { name: /chunks/i })
  await userEvent.click(chunksHeader)
  expect(fileCellOrder()).toEqual(['lease-b.md', 'lease-a.pdf'])
  expect(chunksHeader.closest('th')).toHaveAttribute('aria-sort', 'ascending')

  // Click again: descending → lease-a (4) before lease-b (2).
  await userEvent.click(chunksHeader)
  expect(fileCellOrder()).toEqual(['lease-a.pdf', 'lease-b.md'])
  expect(chunksHeader.closest('th')).toHaveAttribute('aria-sort', 'descending')
})

it('disables "Delete selected" until rows are selected', () => {
  render(<DocumentsTable documents={docs} onOpenDocument={() => {}} onDeleteSelected={() => {}} />)
  expect(screen.getByRole('button', { name: /delete selected \(0\)/i })).toBeDisabled()
})

it('select-all then delete calls onDeleteSelected with every id (after confirm)', async () => {
  const onDeleteSelected = vi.fn()
  const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)
  render(<DocumentsTable documents={docs} onOpenDocument={() => {}} onDeleteSelected={onDeleteSelected} />)

  await userEvent.click(screen.getByRole('checkbox', { name: /select all documents/i }))
  await userEvent.click(screen.getByRole('button', { name: /delete selected \(2\)/i }))

  expect(confirmSpy).toHaveBeenCalledOnce()
  expect(onDeleteSelected).toHaveBeenCalledTimes(1)
  expect(onDeleteSelected.mock.calls[0][0]).toEqual(['d1', 'd2'])
  confirmSpy.mockRestore()
})

it('does not delete when the confirm is cancelled', async () => {
  const onDeleteSelected = vi.fn()
  const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)
  render(<DocumentsTable documents={docs} onOpenDocument={() => {}} onDeleteSelected={onDeleteSelected} />)

  await userEvent.click(screen.getByRole('checkbox', { name: /select lease-a\.pdf/i }))
  await userEvent.click(screen.getByRole('button', { name: /delete selected \(1\)/i }))

  expect(confirmSpy).toHaveBeenCalledOnce()
  expect(onDeleteSelected).not.toHaveBeenCalled()
  confirmSpy.mockRestore()
})
