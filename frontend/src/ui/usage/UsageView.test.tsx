import { beforeEach, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { UsageView } from './UsageView'
import * as client from '../../api/client'
import type { UsageRange, UsageReport } from '../../api/types'

vi.mock('../../api/client')

beforeEach(() => {
  vi.resetAllMocks()
})

const report: UsageReport = {
  range: '24h',
  from: '2026-06-08T12:00:00+00:00',
  to: '2026-06-09T12:00:00+00:00',
  totals: { promptTokens: 170000, completionTokens: 30000, totalTokens: 200000, estimatedCostUsd: 0.037 },
  deployments: [
    { deployment: 'gpt-5.4-mini', promptTokens: 120000, completionTokens: 30000, totalTokens: 150000, estimatedCostUsd: 0.036 },
    { deployment: 'text-embedding-3-small', promptTokens: 50000, completionTokens: 0, totalTokens: 50000, estimatedCostUsd: 0.001 },
  ],
  requests: { total: 400, success: 380, clientErrors: 8, throttled429: 10, serverErrors: 2 },
  latency: { avgMs: 850, maxMs: 4200 },
}

const empty: UsageReport = {
  range: '24h',
  from: '2026-06-08T12:00:00+00:00',
  to: '2026-06-09T12:00:00+00:00',
  totals: { promptTokens: 0, completionTokens: 0, totalTokens: 0, estimatedCostUsd: 0 },
  deployments: [],
  requests: { total: 0, success: 0, clientErrors: 0, throttled429: 0, serverErrors: 0 },
  latency: { avgMs: 0, maxMs: 0 },
}

it('shows a loading state, then renders totals + the per-deployment table', async () => {
  vi.mocked(client.getUsage).mockResolvedValue({ ok: true, value: report })
  render(<UsageView />)

  expect(screen.getByText(/loading usage/i)).toBeInTheDocument()

  // Totals (formatted) + a per-deployment row.
  expect(await screen.findByText('200,000')).toBeInTheDocument()
  expect(screen.getByText('$0.037')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /by deployment/i })).toBeInTheDocument()
  expect(screen.getByText('gpt-5.4-mini')).toBeInTheDocument()
  expect(screen.getByText('text-embedding-3-small')).toBeInTheDocument()

  // Request + latency cards.
  expect(screen.getByRole('heading', { name: /requests/i })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: /latency/i })).toBeInTheDocument()
})

it('renders the empty state when there is no usage in the window', async () => {
  vi.mocked(client.getUsage).mockResolvedValue({ ok: true, value: empty })
  render(<UsageView />)

  expect(await screen.findByText(/no llm usage recorded/i)).toBeInTheDocument()
})

it('renders an error state when the load fails', async () => {
  vi.mocked(client.getUsage).mockResolvedValue({
    ok: false,
    error: { kind: 'server', status: 503, detail: 'monitor down' },
  })
  render(<UsageView />)

  expect(await screen.findByRole('alert')).toHaveTextContent(/could not load usage/i)
})

it('re-queries with the chosen range when a range button is clicked', async () => {
  vi.mocked(client.getUsage).mockResolvedValue({ ok: true, value: report })
  render(<UsageView />)
  await screen.findByText('200,000')

  await userEvent.click(screen.getByRole('button', { name: '7d' }))

  await waitFor(() => {
    const ranges = vi.mocked(client.getUsage).mock.calls.map((c) => c[0] as UsageRange)
    expect(ranges).toContain('7d')
  })
})
