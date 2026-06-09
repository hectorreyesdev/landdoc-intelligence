import { expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ExpirationsWidget } from './ExpirationsWidget'
import type { DocumentSummary } from '../../api/types'

function doc(id: string, fileName: string, fields: ReadonlyArray<readonly [string, string]>): DocumentSummary {
  return {
    id,
    fileName,
    status: 'ready',
    contentType: 'application/pdf',
    chunkCount: 3,
    fields: fields.map(([name, value]) => ({ name, value, sourceChunkId: null })),
    ingestedAt: '2026-06-08T12:00:00.000Z',
  }
}

const NOW = new Date('2026-06-09T00:00:00.000Z')

it('shows an empty state when neither a term nor effective+primary-term is derivable', () => {
  render(
    <ExpirationsWidget
      documents={[doc('d1', 'lease.pdf', [['Lessee', 'Acme']])]}
      onOpenDocument={() => {}}
      now={NOW}
    />,
  )
  expect(screen.getByText(/no lease term to compute yet/i)).toBeInTheDocument()
})

it('derives the term-end from effective date + primary term and marks it estimated', () => {
  render(
    <ExpirationsWidget
      documents={[doc('d1', 'lease.pdf', [['EffectiveDate', '2025-03-01'], ['PrimaryTerm', 'three (3) years']])]}
      onOpenDocument={() => {}}
      now={NOW}
    />,
  )
  // Effective 2025-03-01 + 3 years → 2028-03-01, shown with an "est." marker.
  expect(screen.getByText('2028-03-01')).toBeInTheDocument()
  expect(screen.getByText(/est\./i)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /lease\.pdf/i })).toBeInTheDocument()
})

it('lists upcoming expirations soonest-first and opens a document on click', async () => {
  const onOpenDocument = vi.fn()
  render(
    <ExpirationsWidget
      documents={[
        doc('d-late', 'later.pdf', [['ExpirationDate', '2027-01-01']]),
        doc('d-soon', 'soon.pdf', [['Lease Term End', '2026-06-20']]),
      ]}
      onOpenDocument={onOpenDocument}
      now={NOW}
    />,
  )

  const rowButtons = screen.getAllByRole('button')
  // Soonest first: soon.pdf before later.pdf.
  expect(rowButtons[0]).toHaveTextContent('soon.pdf')

  await userEvent.click(screen.getByRole('button', { name: /soon\.pdf/i }))
  expect(onOpenDocument).toHaveBeenCalledWith('d-soon')
})
