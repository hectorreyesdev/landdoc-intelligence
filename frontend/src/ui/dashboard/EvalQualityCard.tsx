import { Fragment, useState, type ReactElement } from 'react'
import summaryJson from './eval-summary.json'

/** One case's scores + the demo-facing Q&A and grouping metadata in the committed snapshot (spec 0011). */
interface EvalCaseScore {
  id: string
  question: string
  expectedAnswer: string
  expectedSources: readonly string[]
  category: string
  instrument: string
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

/** Display order for the case groupings; any case with an unlisted category falls into a trailing group. */
const CATEGORY_ORDER = [
  'Single-document field lookups',
  'Multi-document / corpus-wide retrieval',
  'Distractor pair (precision)',
  'Abstention / no-hallucination',
] as const

function score(value: number | null, digits: number): string {
  return value === null ? '—' : value.toFixed(digits)
}

function formatDate(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : parsed.toLocaleDateString()
}

/** Group cases by category in display order, with any unknown categories collected into "Other". */
function groupByCategory(cases: readonly EvalCaseScore[]): { category: string; items: EvalCaseScore[] }[] {
  const groups = CATEGORY_ORDER.map((category) => ({
    category: category as string,
    items: cases.filter((c) => c.category === category),
  })).filter((g) => g.items.length > 0)

  const known = new Set<string>(CATEGORY_ORDER)
  const rest = cases.filter((c) => !known.has(c.category))
  if (rest.length > 0) groups.push({ category: 'Other', items: rest })
  return groups
}

/**
 * Read-only scorecard for the latest RAG answer-quality eval run (spec 0011). Renders a committed
 * snapshot (`eval-summary.json`, bundled at build time — no fetch, so the single-fetch invariant holds),
 * so it shows independently of the user's ingested documents. Dated to make staleness obvious.
 *
 * Cases are grouped into sections by what they exercise (single-doc lookup · multi-doc retrieval ·
 * distractor precision · abstention). Each row expands to reveal the actual question, the expected
 * answer, and the source documents — a demo aid: read a question off here, ask it in the Workspace,
 * and compare against the expected answer.
 */
export function EvalQualityCard(): ReactElement {
  const { means, cases, caseCount, judgeModel, generatedAt } = summary
  const [openId, setOpenId] = useState<string | null>(null)
  const [copiedId, setCopiedId] = useState<string | null>(null)
  const groups = groupByCategory(cases)

  function toggle(id: string): void {
    setOpenId((current) => (current === id ? null : id))
  }

  function copyQuestion(id: string, question: string): void {
    void navigator.clipboard?.writeText(question)
    setCopiedId(id)
    window.setTimeout(() => setCopiedId((current) => (current === id ? null : current)), 1500)
  }

  function renderCase(evalCase: EvalCaseScore): ReactElement {
    const isOpen = openId === evalCase.id
    const detailId = `eval-detail-${evalCase.id}`
    return (
      <Fragment key={evalCase.id}>
        <tr className={isOpen ? 'eval-row-open' : undefined}>
          <td className="eval-case-cell">
            <button
              type="button"
              className="eval-row-toggle"
              aria-expanded={isOpen}
              aria-controls={detailId}
              onClick={() => toggle(evalCase.id)}
            >
              <span className="eval-chevron" aria-hidden="true">
                {isOpen ? '▾' : '▸'}
              </span>
              <span className="eval-case-id">{evalCase.id}</span>
              {evalCase.instrument ? <span className="eval-instrument">{evalCase.instrument}</span> : null}
              {evalCase.abstained ? <span className="eval-tag">abstained</span> : null}
            </button>
          </td>
          <td>{score(evalCase.recallAtK, 2)}</td>
          <td>{score(evalCase.groundedness, 0)}</td>
          <td>{score(evalCase.equivalence, 0)}</td>
        </tr>
        {isOpen ? (
          <tr className="eval-detail-row">
            <td colSpan={4}>
              <div className="eval-detail" id={detailId}>
                <dl className="eval-detail-grid">
                  <dt>Question</dt>
                  <dd>{evalCase.question}</dd>
                  <dt>Expected answer</dt>
                  <dd>{evalCase.expectedAnswer}</dd>
                  <dt>Source docs</dt>
                  <dd>
                    {evalCase.abstained
                      ? '— none (abstention case: answer is not in the corpus)'
                      : evalCase.expectedSources.join(', ')}
                  </dd>
                </dl>
                <button
                  type="button"
                  className="eval-copy-btn"
                  onClick={() => copyQuestion(evalCase.id, evalCase.question)}
                >
                  {copiedId === evalCase.id ? 'Copied ✓' : 'Copy question'}
                </button>
              </div>
            </td>
          </tr>
        ) : null}
      </Fragment>
    )
  }

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

      <p className="hint">
        Cases are grouped by what they test. Select a case to see its question, expected answer, and source
        documents.
      </p>

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
          {groups.map((group) => (
            <tbody key={group.category}>
              <tr className="eval-group-header">
                <th scope="colgroup" colSpan={4}>
                  {group.category}
                  <span className="eval-group-count">{group.items.length}</span>
                </th>
              </tr>
              {group.items.map(renderCase)}
            </tbody>
          ))}
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
