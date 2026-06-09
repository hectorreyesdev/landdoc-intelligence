import { useMemo, useState, type ReactElement } from 'react'
import type { DocumentSummary } from '../api/types'
import { documentsToCsv } from './csv'

interface DocumentsTableProps {
  documents: readonly DocumentSummary[]
  /** Open the source-document viewer for a row (spec 0006). */
  onOpenDocument: (documentId: string) => void
  /** Delete the given document ids — called after the user confirms (spec 0008). Defaults to a no-op. */
  onDeleteSelected?: (ids: readonly string[]) => void | Promise<void>
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
 * The persisted documents table (spec 0006): every ingested document with its extracted fields, surviving
 * reload. A "View" opens the original file. Spec 0007 adds search + CSV export; spec 0008 adds row/select-all
 * checkboxes and a confirmed "Delete selected" that removes each document from both stores.
 */
export function DocumentsTable({
  documents,
  onOpenDocument,
  onDeleteSelected = () => {},
}: DocumentsTableProps): ReactElement {
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())

  const filtered = useMemo(() => documents.filter((doc) => matchesQuery(doc, query)), [documents, query])
  const allShownSelected = filtered.length > 0 && filtered.every((doc) => selected.has(doc.id))

  function toggle(id: string): void {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  function toggleAllShown(): void {
    setSelected((prev) => {
      const next = new Set(prev)
      if (allShownSelected) {
        filtered.forEach((doc) => next.delete(doc.id))
      } else {
        filtered.forEach((doc) => next.add(doc.id))
      }
      return next
    })
  }

  async function handleDelete(): Promise<void> {
    const ids = [...selected]
    if (ids.length === 0) {
      return
    }
    const noun = ids.length === 1 ? 'document' : 'documents'
    if (!window.confirm(`Delete ${ids.length} ${noun}? This removes the file and its chunks and can't be undone.`)) {
      return
    }
    await onDeleteSelected(ids)
    setSelected(new Set())
  }

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
              className="doc-delete-button"
              onClick={handleDelete}
              disabled={selected.size === 0}
            >
              Delete selected ({selected.size})
            </button>
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
              <th scope="col" className="doc-select-col">
                <input
                  type="checkbox"
                  aria-label="Select all documents"
                  checked={allShownSelected}
                  onChange={toggleAllShown}
                />
              </th>
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
              <tr key={doc.id} className={selected.has(doc.id) ? 'doc-row--selected' : undefined}>
                <td className="doc-select-col">
                  <input
                    type="checkbox"
                    aria-label={`Select ${doc.fileName}`}
                    checked={selected.has(doc.id)}
                    onChange={() => toggle(doc.id)}
                  />
                </td>
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
