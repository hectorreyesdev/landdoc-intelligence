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
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

/**
 * Lease primary-term tracker (spec 0007): each lease's primary-term end date, bucketed and listed
 * soonest-first. Oil & gas leases carry no explicit expiration date, so the date is normally **derived**
 * from effective date + primary term (an explicit expiration/term-end field is used if one exists). A term
 * ending in the past means the primary term lapsed — not necessarily that the lease is dead (it may be held
 * by production). Honest empty state when neither basis is derivable.
 */
export function ExpirationsWidget({ documents, onOpenDocument, now }: ExpirationsWidgetProps): ReactElement {
  const upcoming = upcomingExpirations(documents, now ?? new Date())

  return (
    <section className="panel dashboard-card" aria-labelledby="expirations-heading">
      <h3 id="expirations-heading">Lease primary term</h3>
      {upcoming.length === 0 ? (
        <p className="doc-empty">
          No lease term to compute yet — needs an effective date + primary term (or an explicit
          expiration/term-end date) in the extracted fields.
        </p>
      ) : (
        <>
          <p className="hint expirations-note">
            Primary-term end = effective date + primary term. Leases past term may be held by production.
          </p>
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
                    <button
                      type="button"
                      className="citation-link"
                      onClick={() => onOpenDocument(item.document.id)}
                    >
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
        </>
      )}
    </section>
  )
}
