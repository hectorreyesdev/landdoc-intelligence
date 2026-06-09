import type { ReactElement } from 'react'
import type { UsageRange, UsageReport } from '../../api/types'
import { useUsage } from './useUsage'
import { formatInt, formatUsd } from './format'

const RANGES: readonly { value: UsageRange; label: string }[] = [
  { value: '24h', label: '24h' },
  { value: '7d', label: '7d' },
  { value: '30d', label: '30d' },
]

/**
 * The Ops / Usage view (spec 0009): an operator-facing read-out of LLM token usage, estimated cost, request
 * health, and latency for the selected range — distinct from the analyst insights Dashboard (spec 0007).
 * Reads live from Azure Monitor metrics via the typed client; renders loading / error / empty states.
 */
export function UsageView(): ReactElement {
  const { status, report, range, setRange } = useUsage('24h')

  return (
    <div className="usage">
      <header className="usage-header">
        <h2>LLM usage &amp; cost</h2>
        <div className="usage-ranges" role="group" aria-label="Time range">
          {RANGES.map((r) => (
            <button
              key={r.value}
              type="button"
              className={r.value === range ? 'tab tab--active' : 'tab'}
              aria-pressed={r.value === range}
              onClick={() => setRange(r.value)}
            >
              {r.label}
            </button>
          ))}
        </div>
      </header>

      {status === 'loading' && <p className="hint">Loading usage…</p>}
      {status === 'error' && (
        <p className="error" role="alert">
          Could not load usage metrics.
        </p>
      )}
      {status === 'ready' && report !== null && <UsageBody report={report} />}
    </div>
  )
}

function UsageBody({ report }: { report: UsageReport }): ReactElement {
  const empty = report.totals.totalTokens === 0 && report.requests.total === 0
  if (empty) {
    return <p className="doc-empty">No LLM usage recorded in this window.</p>
  }

  return (
    <>
      <section className="kpi-row" aria-label="Usage totals">
        <Tile label="Total tokens" value={formatInt(report.totals.totalTokens)} />
        <Tile label="Prompt tokens" value={formatInt(report.totals.promptTokens)} />
        <Tile label="Completion tokens" value={formatInt(report.totals.completionTokens)} />
        <Tile label="Est. cost" value={formatUsd(report.totals.estimatedCostUsd)} />
      </section>
      <p className="hint">
        Cost is an estimate computed from a configured price table — not the Azure invoice.
      </p>

      <section className="panel" aria-labelledby="by-deployment-heading">
        <h3 id="by-deployment-heading">By deployment</h3>
        <table className="doc-table">
          <thead>
            <tr>
              <th scope="col">Deployment</th>
              <th scope="col">Prompt</th>
              <th scope="col">Completion</th>
              <th scope="col">Total</th>
              <th scope="col">Est. cost</th>
            </tr>
          </thead>
          <tbody>
            {report.deployments.map((d) => (
              <tr key={d.deployment}>
                <td>{d.deployment}</td>
                <td>{formatInt(d.promptTokens)}</td>
                <td>{formatInt(d.completionTokens)}</td>
                <td>{formatInt(d.totalTokens)}</td>
                <td>{formatUsd(d.estimatedCostUsd)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <div className="usage-cards">
        <section className="panel" aria-labelledby="requests-heading">
          <h3 id="requests-heading">Requests</h3>
          <dl className="usage-stats">
            <Stat label="Total" value={report.requests.total} />
            <Stat label="Success" value={report.requests.success} />
            <Stat label="4xx" value={report.requests.clientErrors} />
            <Stat label="429" value={report.requests.throttled429} emphasis={report.requests.throttled429 > 0} />
            <Stat label="5xx" value={report.requests.serverErrors} emphasis={report.requests.serverErrors > 0} />
          </dl>
        </section>
        <section className="panel" aria-labelledby="latency-heading">
          <h3 id="latency-heading">Latency</h3>
          <dl className="usage-stats">
            <Stat label="Avg ms" value={Math.round(report.latency.avgMs)} />
            <Stat label="Max ms" value={Math.round(report.latency.maxMs)} />
          </dl>
        </section>
      </div>
    </>
  )
}

function Tile({ label, value }: { label: string; value: string }): ReactElement {
  return (
    <div className="kpi-tile">
      <span className="kpi-value">{value}</span>
      <span className="kpi-label">{label}</span>
    </div>
  )
}

function Stat({ label, value, emphasis = false }: { label: string; value: number; emphasis?: boolean }): ReactElement {
  return (
    <div className={emphasis ? 'usage-stat usage-stat--alert' : 'usage-stat'}>
      <dt>{label}</dt>
      <dd>{formatInt(value)}</dd>
    </div>
  )
}
