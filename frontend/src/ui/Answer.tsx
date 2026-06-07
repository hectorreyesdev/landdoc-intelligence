import type { ReactElement } from 'react'
import type { AskResponse } from '../api/types'

/**
 * The answer-with-citations view (spec 0003 state 4). Only rendered when there is at least
 * one citation — the cite-or-nothing invariant is enforced by the caller (AskPanel), and this
 * component additionally guards against an empty list.
 */
export function Answer({ answer }: { answer: AskResponse }): ReactElement | null {
  if (answer.citations.length === 0) {
    return null
  }
  return (
    <div className="panel-result">
      <h3>Answer</h3>
      <p className="answer-text">{answer.answer}</p>
      <h4>Citations</h4>
      <ul className="citations">
        {answer.citations.map((citation) => (
          <li className="citation" key={citation.chunkId}>
            <span className="citation-meta">
              doc {citation.documentId} · score {citation.score.toFixed(2)}
            </span>
            <blockquote>{citation.text}</blockquote>
          </li>
        ))}
      </ul>
    </div>
  )
}
