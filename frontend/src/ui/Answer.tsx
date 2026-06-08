import type { ReactElement } from 'react'
import type { AskResponse } from '../api/types'

interface AnswerProps {
  answer: AskResponse
  /** Open the source-document viewer for a citation's document (spec 0006). */
  onOpenDocument: (documentId: string) => void
}

/**
 * The answer-with-citations view (spec 0003 state 4). Only rendered when there is at least
 * one citation — the cite-or-nothing invariant is enforced by the caller (AskPanel), and this
 * component additionally guards against an empty list. Each citation is labelled by its source
 * file name (spec 0006) and is a button that opens that document's viewer.
 */
export function Answer({ answer, onOpenDocument }: AnswerProps): ReactElement | null {
  if (answer.citations.length === 0) {
    return null
  }
  return (
    <div className="panel-result answer-result">
      <h3>Answer</h3>
      <p className="answer-text">{answer.answer}</p>
      <h4>Citations</h4>
      <ul className="citations">
        {answer.citations.map((citation) => (
          <li className="citation" key={citation.chunkId}>
            <span className="citation-meta">
              <button
                type="button"
                className="citation-link"
                onClick={() => onOpenDocument(citation.documentId)}
              >
                {citation.source}
              </button>{' '}
              · score {citation.score.toFixed(2)}
            </span>
            <blockquote>{citation.text}</blockquote>
          </li>
        ))}
      </ul>
    </div>
  )
}
