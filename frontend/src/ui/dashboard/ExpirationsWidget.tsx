import type { ReactElement } from 'react'
import type { DocumentSummary } from '../../api/types'
import { type ExpirationBucket, upcomingExpirations } from './metrics'

interface ExpirationsWidgetProps {
  documents: readonly DocumentSummary[]
  onOpenDocument: (documentId: string) => void
  /** Injected in tests for deterministic bucketing; defaults to now. */
  now?: Date
}

const BUCKET_LABEL: Record<ExpirationBucket, string> = {
  expired: 'Expired',
  within30: '≤ 30 days',
  within60: '≤ 60 days',
  within90: '≤ 90 days',
  later: 'Later',
}

function formatDate(date: Date): string {
  return date.toISOString().slice(0, 10)
}

/**
 * Lease-expiration tracker (spec 0007): documents whose extracted fields carry a term/expiration date,
 * bucketed and listed soonest-first. Degrades to an honest empty state when the corpus has no such field
 * (capturing one reliably is a backend extraction follow-up).
 */
export function ExpirationsWidget({ documents, onOpenDocument, now }: ExpirationsWidgetProps): ReactElement {
  const upcoming = upcomingExpirations(documents, now ?? new Date())

  return (
    <section className="panel dashboard-card" aria-labelledby="expirations-heading">
      <h3 id="expirations-heading">Lease expirations</h3>
      {upcoming.length === 0 ? (
        <p className="doc-empty">
          No term/expiration dates found in extracted fields yet. Once the extractor captures a
          term/expiration date, upcoming expirations will appear here.
        </p>
      ) : (
        <table className="doc-table expirations-table">
          <thead>
            <tr>
              <th scope="col">File</th>
              <th scope="col">Expires</th>
              <th scope="col">Days</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {upcoming.map((item) => (
              <tr key={item.document.id}>
                <td>
                  <button
                    type="button"
                    className="citation-link"
                    onClick={() => onOpenDocument(item.document.id)}
                  >
                    {item.document.fileName}
                  </button>
                </td>
                <td>{formatDate(item.date)}</td>
                <td>{item.daysUntil}</td>
                <td>
                  <span className={`expiry-badge expiry-${item.bucket}`}>{BUCKET_LABEL[item.bucket]}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
