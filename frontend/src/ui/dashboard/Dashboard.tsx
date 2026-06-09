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
  ingestByDay,
  needsReviewDocuments,
  summarize,
} from './metrics'
import { ExpirationsWidget } from './ExpirationsWidget'

interface DashboardProps {
  documents: readonly DocumentSummary[]
  status: DocumentTableStatus
  onOpenDocument: (documentId: string) => void
}

function formatIngest(date: Date | null): string {
  return date === null ? '—' : date.toLocaleString()
}

/** The read-only analytics view (spec 0007), aggregated entirely from the GET /documents data. */
export function Dashboard({ documents, status, onOpenDocument }: DashboardProps): ReactElement {
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
  const ingest = ingestByDay(documents)
  const review = needsReviewDocuments(documents)

  return (
    <div className="dashboard">
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
                <Tooltip />
                <Bar dataKey="count" fill="var(--accent)" />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel dashboard-card" aria-labelledby="ingest-heading">
          <h3 id="ingest-heading">Ingest activity</h3>
          <div className="chart-frame">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={[...ingest]}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" />
                <YAxis allowDecimals={false} />
                <Tooltip />
                <Area dataKey="count" stroke="var(--accent)" fill="var(--accent-2)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
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
    </div>
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
