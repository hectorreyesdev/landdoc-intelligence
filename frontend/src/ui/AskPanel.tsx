import { useState, type FormEvent, type ReactElement } from 'react'
import { ask } from '../api/client'
import type { AskResponse } from '../api/types'
import { Answer } from './Answer'
import { describeError } from './errorText'

interface AskPanelProps {
  /** Whether anything has been ingested yet — drives only the hint, not whether asking is allowed. */
  canAsk: boolean
  /** Open the source-document viewer when a citation is clicked (spec 0006). Defaults to a no-op. */
  onOpenDocument?: (documentId: string) => void
}

type AskState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'answered'; answer: AskResponse }
  | { status: 'error'; message: string }

/** Spec 0003 states 3–4: the question box and the grounded answer-with-citations. */
export function AskPanel({ canAsk, onOpenDocument = () => {} }: AskPanelProps): ReactElement {
  const [question, setQuestion] = useState('')
  const [state, setState] = useState<AskState>({ status: 'idle' })

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (question.trim() === '') {
      setState({ status: 'error', message: 'Enter a question.' })
      return
    }
    setState({ status: 'loading' })
    const result = await ask(question)
    if (!result.ok) {
      setState({ status: 'error', message: describeError(result.error, 'ask') })
      return
    }
    // Cite-or-nothing: an answer with no citations is not trustworthy — never show it.
    if (result.value.citations.length === 0) {
      setState({ status: 'error', message: 'No grounded answer — the response carried no citations.' })
      return
    }
    setState({ status: 'answered', answer: result.value })
  }

  const busy = state.status === 'loading'

  return (
    <section className="panel" aria-labelledby="ask-heading">
      <h2 id="ask-heading">Ask a question</h2>
      <form onSubmit={handleSubmit}>
        <input
          type="text"
          aria-label="Question"
          placeholder="Who is the lessee?"
          value={question}
          disabled={busy}
          onChange={(event) => setQuestion(event.target.value)}
        />
        <button type="submit" disabled={busy}>
          {busy ? 'Asking…' : 'Ask'}
        </button>
      </form>
      {!canAsk && <p className="hint">Ingest a document to enable grounded answers.</p>}
      {state.status === 'loading' && (
        <div className="answer-loading" role="status" aria-live="polite">
          <span className="loading-dots" aria-hidden="true">
            <span />
            <span />
            <span />
          </span>
          <span>Searching the corpus…</span>
        </div>
      )}
      {state.status === 'error' && (
        <p className="error" role="alert">
          {state.message}
        </p>
      )}
      {state.status === 'answered' && <Answer answer={state.answer} onOpenDocument={onOpenDocument} />}
    </section>
  )
}
