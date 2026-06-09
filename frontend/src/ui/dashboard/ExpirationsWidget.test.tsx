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

it('shows an empty state when no document has a term/expiration field', () => {
  render(
    <ExpirationsWidget
      documents={[doc('d1', 'lease.pdf', [['Lessee', 'Acme']])]}
      onOpenDocument={() => {}}
      now={NOW}
    />,
  )
  expect(screen.getByText(/no term\/expiration dates found/i)).toBeInTheDocument()
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
