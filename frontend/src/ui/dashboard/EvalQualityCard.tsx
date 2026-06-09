import type { ReactElement } from 'react'
import summaryJson from './eval-summary.json'

/** One case's scores in the committed eval snapshot (spec 0011). */
interface EvalCaseScore {
  id: string
  recallAtK: number | null
  groundedness: number | null
  equivalence: number | null
  abstained: boolean
}

/** Shape of the committed `eval-summary.json` the eval harness emits. */
interface EvalSummary {
  generatedAt: string
  judgeModel: string
  caseCount: number
  means: { recallAtK: number | null; groundedness: number | null; equivalence: number | null }
  cases: readonly EvalCaseScore[]
}

const summary: EvalSummary = summaryJson as EvalSummary

const METHODOLOGY_URL =
  'https://github.com/hectorreyesdev/landdoc-intelligence/blob/main/knowledge/docs/EVAL-HARNESS.md'

function score(value: number | null, digits: number): string {
  return value === null ? '—' : value.toFixed(digits)
}

function formatDate(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : parsed.toLocaleDateString()
}

/**
 * Read-only scorecard for the latest RAG answer-quality eval run (spec 0011). Renders a committed
 * snapshot (`eval-summary.json`, bundled at build time — no fetch, so the single-fetch invariant holds),
 * so it shows independently of the user's ingested documents. Dated to make staleness obvious.
 */
export function EvalQualityCard(): ReactElement {
  const { means, cases, caseCount, judgeModel, generatedAt } = summary

  return (
    <section className="panel dashboard-card eval-card" aria-labelledby="eval-quality-heading">
      <h3 id="eval-quality-heading">Answer quality (eval)</h3>
      <p className="hint">
        {caseCount} cases · judge {judgeModel} · as of {formatDate(generatedAt)}
      </p>

      <div className="kpi-row" aria-label="Eval metric means">
        <div className="kpi-tile">
          <span className="kpi-value">{score(means.recallAtK, 2)}</span>
          <span className="kpi-label">recall@k</span>
        </div>
        <div className="kpi-tile">
          <span className="kpi-value">{score(means.groundedness, 1)}</span>
          <span className="kpi-label">groundedness / 5</span>
        </div>
        <div className="kpi-tile">
          <span className="kpi-value">{score(means.equivalence, 2)}</span>
          <span className="kpi-label">correctness / 5</span>
        </div>
      </div>

      <div className="eval-table-frame">
        <table className="eval-table">
          <thead>
            <tr>
              <th scope="col">Case</th>
              <th scope="col">recall@k</th>
              <th scope="col">grnd</th>
              <th scope="col">equiv</th>
            </tr>
          </thead>
          <tbody>
            {cases.map((evalCase) => (
              <tr key={evalCase.id}>
                <td>
                  {evalCase.id}
                  {evalCase.abstained ? <span className="eval-tag">abstained</span> : null}
                </td>
                <td>{score(evalCase.recallAtK, 2)}</td>
                <td>{score(evalCase.groundedness, 0)}</td>
                <td>{score(evalCase.equivalence, 0)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="hint">
        On-demand eval over a curated corpus — recall@k is deterministic; groundedness and correctness are
        scored by an LLM judge.{' '}
        <a href={METHODOLOGY_URL} target="_blank" rel="noreferrer">
          Methodology ↗
        </a>
      </p>
    </section>
  )
}
