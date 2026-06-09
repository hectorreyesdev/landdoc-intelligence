import { expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Dashboard } from './Dashboard'
import type { DocumentSummary } from '../../api/types'

// Recharts renders nothing meaningful under jsdom; stub the components Dashboard uses to passthroughs so
// the test focuses on the KPI tiles, lists, and widgets (chart correctness is covered by the pure metrics
// tests). Return an explicit object — NOT a Proxy: a Proxy answers every key (incl. `then`), which makes
// the async factory's result look like a thenable and hangs the run forever. No JSX in the hoisted factory.
vi.mock('recharts', async () => {
  const { createElement } = await import('react')
  const Stub = ({ children }: { children?: unknown }) => createElement('div', null, children as never)
  return {
    ResponsiveContainer: Stub,
    BarChart: Stub,
    Bar: Stub,
    AreaChart: Stub,
    Area: Stub,
    CartesianGrid: Stub,
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
    chunkCount: 4,
    fields: fields.map(([name, value]) => ({ name, value, sourceChunkId: null })),
    ingestedAt: '2026-06-08T12:00:00.000Z',
  }
}

const docs: readonly DocumentSummary[] = [
  doc('good', 'good.pdf', [['State', 'Texas'], ['Lessee', 'Acme'], ['ExpirationDate', '2099-01-01']]),
  doc('bad', 'bad.pdf', []), // needs review
]

it('renders KPI sections, the needs-review list, and the expirations widget', () => {
  render(<Dashboard documents={docs} status="ready" onOpenDocument={() => {}} />)

  expect(screen.getByRole('heading', { name: /needs review \(1\)/i })).toBeInTheDocument()
  expect(screen.getByText('bad.pdf')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /lease expirations/i })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /documents by (state|county)/i })).toBeInTheDocument()
})

it('opens a document from the needs-review list', async () => {
  const onOpenDocument = vi.fn()
  render(<Dashboard documents={docs} status="ready" onOpenDocument={onOpenDocument} />)

  await userEvent.click(screen.getByRole('button', { name: /bad\.pdf/i }))
  expect(onOpenDocument).toHaveBeenCalledWith('bad')
})

it('shows loading and empty states', () => {
  const { rerender } = render(<Dashboard documents={[]} status="loading" onOpenDocument={() => {}} />)
  expect(screen.getByText(/loading dashboard/i)).toBeInTheDocument()

  rerender(<Dashboard documents={[]} status="ready" onOpenDocument={() => {}} />)
  expect(screen.getByText(/no documents yet/i)).toBeInTheDocument()
})
