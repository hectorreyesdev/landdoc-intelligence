import { expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LeaseTermWidget } from './LeaseTermWidget'
import type { DocumentSummary } from '../../api/types'

// Recharts renders nothing meaningful under jsdom; stub the pieces the chart views use so view-switching
// is testable. The Table + Heatmap views are plain DOM and are asserted directly.
vi.mock('recharts', async () => {
  const { createElement } = await import('react')
  const Stub = ({ children }: { children?: unknown }) => createElement('div', null, children as never)
  return {
    Bar: Stub,
    BarChart: Stub,
    CartesianGrid: Stub,
    Cell: Stub,
    ReferenceLine: Stub,
    ResponsiveContainer: Stub,
    Scatter: Stub,
    ScatterChart: Stub,
    Tooltip: Stub,
    XAxis: Stub,
    YAxis: Stub,
  }
})

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
const lease = doc('d1', 'reeves-tx.pdf', [['EffectiveDate', '2025-03-01'], ['PrimaryTerm', 'three (3) years']])

it('shows an empty state when no lease term is derivable', () => {
  render(<LeaseTermWidget documents={[doc('x', 'a.pdf', [['Lessee', 'Acme']])]} onOpenDocument={() => {}} now={NOW} />)
  expect(screen.getByText(/no lease term to compute yet/i)).toBeInTheDocument()
})

it('defaults to the Table view with the derived term-end and opens a document on click', async () => {
  const onOpenDocument = vi.fn()
  render(<LeaseTermWidget documents={[lease]} onOpenDocument={onOpenDocument} now={NOW} />)

  // Effective 2025-03-01 + 3 years → 2028-03-01.
  expect(screen.getByText('2028-03-01')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: /reeves-tx\.pdf/i }))
  expect(onOpenDocument).toHaveBeenCalledWith('d1')
})

it('switches views via the segmented control', async () => {
  render(<LeaseTermWidget documents={[lease]} onOpenDocument={() => {}} now={NOW} />)

  await userEvent.click(screen.getByRole('tab', { name: 'Heatmap' }))
  expect(screen.getByRole('tab', { name: 'Heatmap' })).toHaveAttribute('aria-selected', 'true')
  // 2028-03-01 falls in Q1 2028 → a cell shows the count and a descriptive title.
  expect(screen.getByTitle(/1 lease end[s]? in Q1 2028/i)).toBeInTheDocument()

  await userEvent.click(screen.getByRole('tab', { name: 'Timeline' }))
  expect(screen.getByRole('tab', { name: 'Timeline' })).toHaveAttribute('aria-selected', 'true')
})
