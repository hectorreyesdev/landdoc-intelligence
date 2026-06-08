import type { ReactElement } from 'react'
import type { DocumentSummary } from '../api/types'

interface DocumentsTableProps {
  documents: readonly DocumentSummary[]
  /** Open the source-document viewer for a row (spec 0006). */
  onOpenDocument: (documentId: string) => void
}

function formatIngested(iso: string): string {
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString()
}

/**
 * The persisted documents table (spec 0006): every ingested document with its extracted fields, shown
 * alongside the session upload grid and surviving reload. Each row's "View" opens the original file.
 */
export function DocumentsTable({ documents, onOpenDocument }: DocumentsTableProps): ReactElement {
  return (
    <section className="panel doc-table-panel" aria-labelledby="doc-table-heading">
      <h2 id="doc-table-heading">All documents ({documents.length})</h2>
      {documents.length === 0 ? (
        <p className="doc-empty">No documents ingested yet.</p>
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
            {documents.map((doc) => (
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
