import type { ReactElement } from 'react'
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { DocumentSummary } from '../../api/types'
import type { DocumentTableStatus } from '../useDocumentTable'
import {
  documentsByCounty,
  documentsByState,
  documentsByStateCounty,
  ingestByHour,
  needsReviewDocuments,
  summarize,
} from './metrics'
import { ExpirationsWidget } from './ExpirationsWidget'
import { EvalQualityCard } from './EvalQualityCard'
import { CountyMap } from './CountyMap'

interface DashboardProps {
  documents: readonly DocumentSummary[]
  status: DocumentTableStatus
  onOpenDocument: (documentId: string) => void
}

function formatIngest(date: Date | null): string {
  return date === null ? '—' : date.toLocaleString()
}

/** "2026-06-09T03:00" → "06-09 03:00" — compact axis/tooltip label for the hourly ingest series. */
function formatHour(hour: string): string {
  const match = /^\d{4}-(\d{2}-\d{2})T(\d{2}:\d{2})$/.exec(hour)
  return match === null ? hour : `${match[1]} ${match[2]}`
}

// Recharts renders the tooltip as a floating div with default light styling (white background, light label),
// which is unreadable in dark mode. Pin it to the theme tokens so it adapts to both themes.
const TOOLTIP_CONTENT_STYLE = { background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 8 }
const TOOLTIP_LABEL_STYLE = { color: 'var(--heading)' }

/**
 * The read-only analytics view (spec 0007). The corpus analytics (aggregated from GET /documents) lead;
 * the answer-quality eval scorecard (spec 0011) renders at the bottom and is independent of the document list.
 */
export function Dashboard({ documents, status, onOpenDocument }: DashboardProps): ReactElement {
  return (
    <div className="dashboard">
      <DashboardCorpus documents={documents} status={status} onOpenDocument={onOpenDocument} />
      <EvalQualityCard />
    </div>
  )
}

/** Corpus analytics aggregated entirely from the GET /documents data (spec 0007). */
function DashboardCorpus({ documents, status, onOpenDocument }: DashboardProps): ReactElement {
  if (status === 'loading') {
    return <p className="hint">Loading dashboard…</p>
  }
  if (status === 'error') {
    return (
      <p className="error" role="alert">
        Could not load the document list.
      </p>
    )
  }
  if (documents.length === 0) {
    return <p className="doc-empty">No documents yet — ingest one to populate the dashboard.</p>
  }

  const summary = summarize(documents)
  const byState = documentsByState(documents)
  const byCounty = documentsByCounty(documents)
  // Show whichever location field the extractor actually populated (more distinct values = more useful).
  const location = byState.length >= byCounty.length ? byState : byCounty
  const locationLabel = location === byState ? 'state' : 'county'
  const ingest = ingestByHour(documents)
  const byStateCounty = documentsByStateCounty(documents)
  const review = needsReviewDocuments(documents)

  return (
    <>
      <section className="kpi-row" aria-label="Corpus overview">
        <KpiTile label="Documents" value={summary.totalDocuments} />
        <KpiTile label="Chunks" value={summary.totalChunks} />
        <KpiTile label="States" value={summary.distinctStates} />
        <KpiTile label="Counties" value={summary.distinctCounties} />
        <KpiTile label="Needs review" value={summary.needsReviewCount} emphasis={summary.needsReviewCount > 0} />
        <KpiTile label="Latest ingest" text={formatIngest(summary.latestIngest)} />
      </section>

      <div className="dashboard-grid">
        <section className="panel dashboard-card" aria-labelledby="by-location-heading">
          <h3 id="by-location-heading">Documents by {locationLabel}</h3>
          <div className="chart-frame">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={[...location]}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="label" />
                <YAxis allowDecimals={false} />
                <Tooltip contentStyle={TOOLTIP_CONTENT_STYLE} labelStyle={TOOLTIP_LABEL_STYLE} />
                <Bar dataKey="count" fill="var(--accent)" />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel dashboard-card" aria-labelledby="ingest-heading">
          <h3 id="ingest-heading">Ingest activity (by hour)</h3>
          <div className="chart-frame">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={[...ingest]}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="hour" tickFormatter={(value) => formatHour(String(value))} minTickGap={24} />
                <YAxis allowDecimals={false} />
                <Tooltip
                  labelFormatter={(label) => formatHour(String(label))}
                  contentStyle={TOOLTIP_CONTENT_STYLE}
                  labelStyle={TOOLTIP_LABEL_STYLE}
                />
                <Area dataKey="count" stroke="var(--accent)" fill="var(--accent-2)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel dashboard-card" aria-labelledby="map-heading">
          <h3 id="map-heading">Documents by county (map)</h3>
          <CountyMap locations={byStateCounty} />
        </section>

        <ExpirationsWidget documents={documents} onOpenDocument={onOpenDocument} />

        <section className="panel dashboard-card" aria-labelledby="review-heading">
          <h3 id="review-heading">Needs review ({review.length})</h3>
          {review.length === 0 ? (
            <p className="doc-empty">Every document has structured fields. 🎉</p>
          ) : (
            <ul className="review-list">
              {review.map((doc) => (
                <li key={doc.id}>
                  <button type="button" className="citation-link" onClick={() => onOpenDocument(doc.id)}>
                    {doc.fileName}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </>
  )
}

function KpiTile({
  label,
  value,
  text,
  emphasis = false,
}: {
  label: string
  value?: number
  text?: string
  emphasis?: boolean
}): ReactElement {
  return (
    <div className={emphasis ? 'kpi-tile kpi-tile--alert' : 'kpi-tile'}>
      <span className="kpi-value">{text ?? value}</span>
      <span className="kpi-label">{label}</span>
    </div>
  )
}
