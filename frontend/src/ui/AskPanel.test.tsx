import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AskPanel } from './AskPanel'
import * as client from '../api/client'
import type { ApiErrorKind, ApiResult } from '../api/client'
import type { AskResponse } from '../api/types'

vi.mock('../api/client')

beforeEach(() => {
  vi.resetAllMocks()
})

function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((r) => {
    resolve = r
  })
  return { promise, resolve }
}

const answer: AskResponse = {
  answer: 'The lessee is Acme Minerals LLC.',
  citations: [{ chunkId: 'c1', documentId: 'doc-1', score: 0.82, text: '…by and between … as Lessee …' }],
}

async function submitQuestion(text = 'Who is the lessee?'): Promise<void> {
  await userEvent.type(screen.getByRole('textbox', { name: /question/i }), text)
  await userEvent.click(screen.getByRole('button', { name: /^ask$/i }))
}

it('renders the answer and each citation (text, score, documentId)', async () => {
  vi.mocked(client.ask).mockResolvedValue({ ok: true, value: answer })
  render(<AskPanel canAsk />)

  await submitQuestion()

  expect(await screen.findByText(/acme minerals llc/i)).toBeInTheDocument()
  expect(screen.getByText(/as Lessee/)).toBeInTheDocument()
  expect(screen.getByText(/doc-1/)).toBeInTheDocument()
  expect(screen.getByText(/0\.82/)).toBeInTheDocument()
})

it('shows an animated loading indicator while the answer is pending, then the answer', async () => {
  const pending = deferred<ApiResult<AskResponse>>()
  vi.mocked(client.ask).mockReturnValue(pending.promise)
  render(<AskPanel canAsk />)

  await submitQuestion()

  expect(await screen.findByText(/searching the corpus/i)).toBeInTheDocument()

  await act(async () => {
    pending.resolve({ ok: true, value: answer })
  })

  expect(await screen.findByText(/acme minerals llc/i)).toBeInTheDocument()
  expect(screen.queryByText(/searching the corpus/i)).not.toBeInTheDocument()
})

it('never renders an answer without a citation (cite-or-nothing)', async () => {
  vi.mocked(client.ask).mockResolvedValue({ ok: true, value: { answer: 'orphan answer', citations: [] } })
  render(<AskPanel canAsk />)

  await submitQuestion()

  expect(await screen.findByRole('alert')).toHaveTextContent(/no grounded answer|no citations/i)
  expect(screen.queryByText('orphan answer')).not.toBeInTheDocument()
})

it('validates a blank question client-side without calling the client', async () => {
  render(<AskPanel canAsk />)

  await userEvent.click(screen.getByRole('button', { name: /^ask$/i }))

  expect(screen.getByRole('alert')).toHaveTextContent(/enter a question/i)
  expect(client.ask).not.toHaveBeenCalled()
})

describe('degradation — one distinct, non-crashing state per status', () => {
  function arrangeError(kind: ApiErrorKind, status: number | null): void {
    vi.mocked(client.ask).mockResolvedValue({ ok: false, error: { kind, status, detail: null } })
  }

  it('400 → inline validation; the form stays usable and no answer renders', async () => {
    arrangeError('validation', 400)
    render(<AskPanel canAsk />)

    await submitQuestion('x')

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^ask$/i })).toBeEnabled()
    expect(screen.queryByText(/answer/i)).not.toBeInTheDocument()
  })

  it('409 → "ingest a document first" state', async () => {
    arrangeError('empty-store', 409)
    render(<AskPanel canAsk />)

    await submitQuestion()

    expect(await screen.findByRole('alert')).toHaveTextContent(/ingest a document first/i)
  })

  it('501 → "Q&A not available" state (defensive — /ask is live today)', async () => {
    arrangeError('not-implemented', 501)
    render(<AskPanel canAsk />)

    await submitQuestion()

    expect(await screen.findByRole('alert')).toHaveTextContent(/not available/i)
  })

  it('network/5xx → generic retryable error', async () => {
    arrangeError('network', null)
    render(<AskPanel canAsk />)

    await submitQuestion()

    expect(await screen.findByRole('alert')).toHaveTextContent(/reach the server|try again/i)
  })
})

it('shows a hint (not a block) before anything is ingested', () => {
  render(<AskPanel canAsk={false} />)
  expect(screen.getByText(/ingest a document to enable/i)).toBeInTheDocument()
  // asking is still allowed pre-ingest — the button is not disabled
  expect(screen.getByRole('button', { name: /^ask$/i })).toBeEnabled()
})
