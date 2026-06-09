import { useState, type ReactElement } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ReferenceLine,
  ResponsiveContainer,
  Scatter,
  ScatterChart,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { DocumentSummary } from '../../api/types'
import { type ExpirationBucket, type UpcomingExpiration, upcomingExpirations } from './metrics'

/** Minimal shape of the props Recharts injects into a custom Tooltip `content` component. */
interface ChartTooltipProps {
  active?: boolean
  payload?: ReadonlyArray<{ payload: Record<string, unknown> }>
}

interface LeaseTermWidgetProps {
  documents: readonly DocumentSummary[]
  onOpenDocument: (documentId: string) => void
  /** Injected in tests for deterministic bucketing; defaults to now. */
  now?: Date
}

type LeaseView = 'table' | 'gantt' | 'runway' | 'heatmap'

const VIEWS: ReadonlyArray<{ key: LeaseView; label: string }> = [
  { key: 'table', label: 'Table' },
  { key: 'gantt', label: 'Timeline' },
  { key: 'runway', label: 'Runway' },
  { key: 'heatmap', label: 'Heatmap' },
]

const BUCKET_LABEL: Record<ExpirationBucket, string> = {
  expired: 'Expired',
  within30: '≤ 30 days',
  within60: '≤ 60 days',
  within90: '≤ 90 days',
  later: 'Later',
}

const BUCKET_FILL: Record<ExpirationBucket, string> = {
  expired: 'var(--error)',
  within30: 'var(--accent-2)',
  within60: 'var(--accent-2)',
  within90: 'var(--accent-2)',
  later: 'var(--accent)',
}

const DAY_MS = 86_400_000
const TOOLTIP_STYLE = { background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 8 }

function formatDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

/**
 * Lease primary-term tracker (spec 0007). Each lease's primary-term end (effective date + primary term,
 * or an explicit term-end field) shown four ways — a detail Table, a Gantt Timeline of each term's span, an
 * Expiration Runway of term-end dates, and a year×quarter Heatmap of expiry density — switchable in-widget.
 * A term ending in the past means the primary term lapsed, not that the lease is dead (may be held by
 * production). Honest empty state when nothing is derivable.
 */
export function LeaseTermWidget({ documents, onOpenDocument, now }: LeaseTermWidgetProps): ReactElement {
  const [view, setView] = useState<LeaseView>('table')
  const reference = now ?? new Date()
  const upcoming = upcomingExpirations(documents, reference)

  return (
    <section className="panel dashboard-card lease-term-card" aria-labelledby="lease-term-heading">
      <div className="lease-term-header">
        <h3 id="lease-term-heading">Lease primary term</h3>
        {upcoming.length > 0 && (
          <div className="seg-control" role="tablist" aria-label="Lease term view">
            {VIEWS.map((v) => (
              <button
                key={v.key}
                type="button"
                role="tab"
                aria-selected={view === v.key}
                className={view === v.key ? 'seg-button seg-button--active' : 'seg-button'}
                onClick={() => setView(v.key)}
              >
                {v.label}
              </button>
            ))}
          </div>
        )}
      </div>

      <p className="hint expirations-note">
        Primary-term end = effective date + primary term. Leases past term may be held by production.
      </p>

      {upcoming.length === 0 ? (
        <p className="doc-empty">
          No lease term to compute yet — needs an effective date + primary term (or an explicit
          expiration/term-end date) in the extracted fields.
        </p>
      ) : view === 'table' ? (
        <TableView upcoming={upcoming} onOpenDocument={onOpenDocument} />
      ) : view === 'gantt' ? (
        <GanttView upcoming={upcoming} now={reference} />
      ) : view === 'runway' ? (
        <RunwayView upcoming={upcoming} now={reference} />
      ) : (
        <HeatmapView upcoming={upcoming} />
      )}
    </section>
  )
}

function TableView({
  upcoming,
  onOpenDocument,
}: {
  upcoming: readonly UpcomingExpiration[]
  onOpenDocument: (documentId: string) => void
}): ReactElement {
  return (
    <table className="doc-table expirations-table">
      <thead>
        <tr>
          <th scope="col">File</th>
          <th scope="col">Term ends</th>
          <th scope="col">Days</th>
          <th scope="col">Status</th>
        </tr>
      </thead>
      <tbody>
        {upcoming.map((item) => (
          <tr key={item.document.id}>
            <td>
              <button type="button" className="citation-link" onClick={() => onOpenDocument(item.document.id)}>
                {item.document.fileName}
              </button>
            </td>
            <td>
              <span>{formatDate(item.date)}</span>
              {item.basis === 'derived' && (
                <span className="expiry-est" title="computed from effective date + primary term"> est.</span>
              )}
            </td>
            <td>{item.daysUntil}</td>
            <td>
              <span className={`expiry-badge expiry-${item.bucket}`}>{BUCKET_LABEL[item.bucket]}</span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

/** Shorten a long file name for a chart axis label. */
function shortName(fileName: string): string {
  return fileName.replace(/\.(pdf|md|txt)$/i, '')
}

function GanttView({ upcoming, now }: { upcoming: readonly UpcomingExpiration[]; now: Date }): ReactElement {
  // Each lease with a known start (effective date) becomes a bar spanning start → term-end.
  const spans = upcoming.filter((u) => u.start !== null)
  if (spans.length === 0) {
    return <p className="doc-empty">No lease has both an effective date and primary term to chart a span.</p>
  }
  const minMs = Math.min(...spans.map((u) => (u.start as Date).getTime()))
  const maxMs = Math.max(...spans.map((u) => u.date.getTime()))
  const data = spans.map((u) => {
    const startMs = (u.start as Date).getTime()
    return {
      name: shortName(u.document.fileName),
      offset: (startMs - minMs) / DAY_MS,
      duration: Math.max((u.date.getTime() - startMs) / DAY_MS, 1),
      bucket: u.bucket,
      startLabel: formatDate(u.start as Date),
      endLabel: formatDate(u.date),
    }
  })
  const spanDays = (maxMs - minMs) / DAY_MS
  const yearTick = (offsetDays: number): string => String(new Date(minMs + offsetDays * DAY_MS).getFullYear())
  const todayOffset = (now.getTime() - minMs) / DAY_MS
  const height = Math.max(180, data.length * 26 + 48)

  return (
    <div className="lease-chart-frame" style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart layout="vertical" data={data} margin={{ top: 8, right: 16, bottom: 8, left: 8 }}>
          <CartesianGrid strokeDasharray="3 3" horizontal={false} />
          <XAxis type="number" domain={[0, spanDays]} tickFormatter={yearTick} />
          <YAxis type="category" dataKey="name" width={150} tick={{ fontSize: 11 }} />
          <Tooltip content={<GanttTooltip />} />
          {todayOffset >= 0 && todayOffset <= spanDays && (
            <ReferenceLine x={todayOffset} stroke="var(--error)" strokeDasharray="4 2" label={{ value: 'today', fontSize: 10, fill: 'var(--error)' }} />
          )}
          <Bar dataKey="offset" stackId="term" fill="transparent" isAnimationActive={false} />
          <Bar dataKey="duration" stackId="term" radius={[2, 2, 2, 2]} isAnimationActive={false}>
            {data.map((d, i) => (
              <Cell key={i} fill={BUCKET_FILL[d.bucket]} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}

function GanttTooltip({ active, payload }: ChartTooltipProps): ReactElement | null {
  if (active !== true || payload === undefined || payload.length === 0) {
    return null
  }
  const d = payload[payload.length - 1]?.payload as unknown as { name: string; startLabel: string; endLabel: string }
  return (
    <div className="chart-tooltip" style={TOOLTIP_STYLE}>
      <strong>{d.name}</strong>
      <div>{d.startLabel} → {d.endLabel}</div>
    </div>
  )
}

function RunwayView({ upcoming, now }: { upcoming: readonly UpcomingExpiration[]; now: Date }): ReactElement {
  const data = upcoming.map((u) => ({
    x: u.date.getTime(),
    y: 1,
    name: shortName(u.document.fileName),
    endLabel: formatDate(u.date),
    bucket: u.bucket,
  }))
  const yearTick = (ms: number): string => String(new Date(ms).getFullYear())

  return (
    <div className="lease-chart-frame" style={{ height: 200 }}>
      <ResponsiveContainer width="100%" height="100%">
        <ScatterChart margin={{ top: 16, right: 24, bottom: 16, left: 16 }}>
          <CartesianGrid strokeDasharray="3 3" vertical horizontal={false} />
          <XAxis type="number" dataKey="x" domain={['dataMin', 'dataMax']} tickFormatter={yearTick} name="Term end" />
          <YAxis type="number" dataKey="y" domain={[0, 2]} hide />
          <Tooltip content={<RunwayTooltip />} cursor={{ strokeDasharray: '3 3' }} />
          <ReferenceLine x={now.getTime()} stroke="var(--error)" strokeDasharray="4 2" label={{ value: 'today', fontSize: 10, fill: 'var(--error)' }} />
          <Scatter data={data} isAnimationActive={false}>
            {data.map((d, i) => (
              <Cell key={i} fill={BUCKET_FILL[d.bucket]} />
            ))}
          </Scatter>
        </ScatterChart>
      </ResponsiveContainer>
    </div>
  )
}

function RunwayTooltip({ active, payload }: ChartTooltipProps): ReactElement | null {
  if (active !== true || payload === undefined || payload.length === 0) {
    return null
  }
  const d = payload[0]?.payload as unknown as { name: string; endLabel: string }
  return (
    <div className="chart-tooltip" style={TOOLTIP_STYLE}>
      <strong>{d.name}</strong>
      <div>ends {d.endLabel}</div>
    </div>
  )
}

const QUARTERS = ['Q1', 'Q2', 'Q3', 'Q4'] as const

function HeatmapView({ upcoming }: { upcoming: readonly UpcomingExpiration[] }): ReactElement {
  const years = [...new Set(upcoming.map((u) => u.date.getFullYear()))].sort((a, b) => a - b)
  // counts[year][quarterIndex]
  const counts = new Map<number, number[]>()
  for (const year of years) {
    counts.set(year, [0, 0, 0, 0])
  }
  for (const u of upcoming) {
    const q = Math.floor(u.date.getMonth() / 3)
    counts.get(u.date.getFullYear())![q] += 1
  }
  const max = Math.max(1, ...[...counts.values()].flat())

  return (
    <div className="heatmap" role="table" aria-label="Lease term ends by year and quarter">
      <div className="heatmap-row heatmap-head" role="row">
        <span className="heatmap-corner" role="columnheader" />
        {QUARTERS.map((q) => (
          <span key={q} className="heatmap-q" role="columnheader">{q}</span>
        ))}
      </div>
      {years.map((year) => (
        <div key={year} className="heatmap-row" role="row">
          <span className="heatmap-year" role="rowheader">{year}</span>
          {(counts.get(year) as number[]).map((n, qi) => (
            <span
              key={qi}
              className="heatmap-cell"
              role="cell"
              title={`${n} lease${n === 1 ? '' : 's'} end in ${QUARTERS[qi]} ${year}`}
              style={{ background: n === 0 ? 'var(--surface-2)' : `color-mix(in srgb, var(--accent) ${Math.round((n / max) * 85) + 15}%, transparent)` }}
            >
              {n > 0 ? n : ''}
            </span>
          ))}
        </div>
      ))}
    </div>
  )
}
