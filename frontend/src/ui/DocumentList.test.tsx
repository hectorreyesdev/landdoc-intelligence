import { expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { DocumentList } from './DocumentList'
import type { DocItem } from './useDocuments'
import type { DocumentResponse } from '../api/types'

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

function ready(key: string, fileName: string, fields = doc().fields): DocItem {
  return { key, status: 'ready', fileName, doc: doc({ fileName, fields }) }
}

it('renders nothing until a document is ingested', () => {
  const { container } = render(<DocumentList items={[]} />)
  expect(container).toBeEmptyDOMElement()
})

it('titles each card with the file name and shows the document count', () => {
  render(<DocumentList items={[ready('a', 'a.pdf'), ready('b', 'b.md')]} />)

  expect(screen.getByRole('heading', { name: /documents \(2\)/i })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'a.pdf' })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'b.md' })).toBeInTheDocument()
})

it('shows extracted fields and chunk count on a ready tile, without an "Extracted fields" heading', () => {
  render(<DocumentList items={[ready('a', 'lease.pdf')]} />)

  expect(screen.getByText(/3 chunks/i)).toBeInTheDocument()
  expect(screen.getByText('Lessee')).toBeInTheDocument()
  expect(screen.getByText('Acme Minerals LLC')).toBeInTheDocument()
  expect(screen.queryByText(/extracted fields/i)).not.toBeInTheDocument()
})

it('shows a muted note when a ready tile has no fields', () => {
  render(<DocumentList items={[ready('a', 'lease.pdf', [])]} />)
  expect(screen.getByText(/no fields/i)).toBeInTheDocument()
})

it('renders a grayed, busy placeholder tile for an in-flight upload', () => {
  render(<DocumentList items={[{ key: 'p', status: 'uploading', fileName: 'incoming.pdf' }]} />)

  expect(screen.getByRole('heading', { name: 'incoming.pdf' })).toBeInTheDocument()
  expect(screen.getByText(/uploading/i)).toBeInTheDocument()
  expect(screen.getByRole('article')).toHaveAttribute('aria-busy', 'true')
})

it('renders an error tile for a failed upload', () => {
  render(<DocumentList items={[{ key: 'e', status: 'error', fileName: 'bad.pdf', message: 'Something went wrong. Please try again.' }]} />)

  expect(screen.getByRole('heading', { name: 'bad.pdf' })).toBeInTheDocument()
  expect(screen.getByText(/upload failed/i)).toBeInTheDocument()
  expect(screen.getByText(/went wrong/i)).toBeInTheDocument()
})
