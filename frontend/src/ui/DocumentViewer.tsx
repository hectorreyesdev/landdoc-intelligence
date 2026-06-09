import { useEffect, useState, type ReactElement } from 'react'
import { documentFileUrl, getDocument } from '../api/client'
import type { DocumentSummary } from '../api/types'

interface DocumentViewerProps {
  documentId: string
  onClose: () => void
}

type ViewerState =
  | { status: 'loading' }
  | { status: 'ready'; document: DocumentSummary }
  | { status: 'error' }

/**
 * Modal viewer for a source document (spec 0006): shows the extracted fields and embeds the ORIGINAL
 * uploaded file in an `<iframe>` (the browser renders PDF and text inline). The file URL is a plain
 * same-origin string — no fetch here, so the single-typed-client invariant holds. Closes on the
 * backdrop, the × button, or Escape.
 */
export function DocumentViewer({ documentId, onClose }: DocumentViewerProps): ReactElement {
  const [state, setState] = useState<ViewerState>({ status: 'loading' })

  useEffect(() => {
    let active = true
    setState({ status: 'loading' })
    void getDocument(documentId).then((result) => {
      if (!active) {
        return
      }
      setState(result.ok ? { status: 'ready', document: result.value } : { status: 'error' })
    })
    return () => {
      active = false
    }
  }, [documentId])

  useEffect(() => {
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const title = state.status === 'ready' ? state.document.fileName : 'Document'

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        className="modal modal--wide"
        role="dialog"
        aria-modal="true"
        aria-label={`Document: ${title}`}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="modal-header">
          <h2 className="modal-title" title={title}>
            {title}
          </h2>
          <button type="button" className="modal-close" aria-label="Close" onClick={onClose}>
            ×
          </button>
        </header>

        {state.status === 'loading' && <p className="hint">Loading document…</p>}
        {state.status === 'error' && (
          <p className="error" role="alert">
            Could not load this document.
          </p>
        )}
        {state.status === 'ready' && (
          <div className="viewer-body">
            {state.document.fields.length > 0 && (
              <dl className="fields">
                {state.document.fields.map((field) => (
                  <div className="field" key={field.name}>
                    <dt>{field.name}</dt>
                    <dd>{field.value}</dd>
                  </div>
                ))}
              </dl>
            )}
            <iframe
              className="viewer-frame"
              src={documentFileUrl(documentId)}
              title={`Source file: ${title}`}
            />
          </div>
        )}
      </div>
    </div>
  )
}
