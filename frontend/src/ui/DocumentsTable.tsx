import { useMemo, useState, type ReactElement } from 'react'
import type { DocumentSummary } from '../api/types'
import { documentsToCsv } from './csv'

interface DocumentsTableProps {
  documents: readonly DocumentSummary[]
  /** Open the source-document viewer for a row (spec 0006). */
  onOpenDocument: (documentId: string) => void
}

function formatIngested(iso: string): string {
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString()
}

/** Case-insensitive match against file name + every field name/value (spec 0007 search). */
function matchesQuery(doc: DocumentSummary, query: string): boolean {
  const q = query.trim().toLowerCase()
  if (q === '') {
    return true
  }
  if (doc.fileName.toLowerCase().includes(q)) {
    return true
  }
  return doc.fields.some(
    (field) => field.name.toLowerCase().includes(q) || field.value.toLowerCase().includes(q),
  )
}

function downloadCsv(docs: readonly DocumentSummary[]): void {
  const blob = new Blob([documentsToCsv(docs)], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = 'landdoc-documents.csv'
  anchor.click()
  URL.revokeObjectURL(url)
}

/**
 * The persisted documents table (spec 0006): every ingested document with its extracted fields, shown
 * alongside the session upload grid and surviving reload. A "View" opens the original file. Spec 0007 adds
 * a search filter (file name + field name/value) and CSV export of the currently-shown rows.
 */
export function DocumentsTable({ documents, onOpenDocument }: DocumentsTableProps): ReactElement {
  const [query, setQuery] = useState('')
  const filtered = useMemo(() => documents.filter((doc) => matchesQuery(doc, query)), [documents, query])

  return (
    <section className="panel doc-table-panel" aria-labelledby="doc-table-heading">
      <div className="doc-table-header">
        <h2 id="doc-table-heading">All documents ({filtered.length})</h2>
        {documents.length > 0 && (
          <div className="doc-table-tools">
            <input
              type="search"
              aria-label="Search documents"
              placeholder="Search file name or fields…"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
            <button
              type="button"
              className="doc-export-button"
              onClick={() => downloadCsv(filtered)}
              disabled={filtered.length === 0}
            >
              Export CSV
            </button>
          </div>
        )}
      </div>

      {documents.length === 0 ? (
        <p className="doc-empty">No documents ingested yet.</p>
      ) : filtered.length === 0 ? (
        <p className="doc-empty">No documents match “{query}”.</p>
      ) : (
        <table className="doc-table">
          <thead>
            <tr>
              <th scope="col">File</th>
              <th scope="col">Status</th>
              <th scope="col">Chunks</th>
              <th scope="col">Fields</th>
              <th scope="col">Ingested</th>
              <th scope="col">View</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((doc) => (
              <tr key={doc.id}>
                <td className="doc-table-file" title={doc.fileName}>
                  {doc.fileName}
                </td>
                <td>{doc.status}</td>
                <td>{doc.chunkCount}</td>
                <td>
                  {doc.fields.length === 0 ? (
                    <span className="doc-empty">—</span>
                  ) : (
                    <ul className="doc-table-fields">
                      {doc.fields.map((field) => (
                        <li key={field.name}>
                          <span className="field-name">{field.name}:</span> {field.value}
                        </li>
                      ))}
                    </ul>
                  )}
                </td>
                <td>{formatIngested(doc.ingestedAt)}</td>
                <td>
                  <button
                    type="button"
                    className="doc-view-button"
                    onClick={() => onOpenDocument(doc.id)}
                  >
                    View
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
