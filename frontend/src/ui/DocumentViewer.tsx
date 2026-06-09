import { useEffect, useState, type ReactElement } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { documentFileUrl, getDocument, getDocumentFileText } from '../api/client'
import type { DocumentSummary } from '../api/types'

interface DocumentViewerProps {
  documentId: string
  onClose: () => void
}

type ViewerState =
  | { status: 'loading' }
  | { status: 'ready'; document: DocumentSummary }
  | { status: 'error' }

/** Markdown uploads render as formatted HTML; everything else (PDF, plain text) embeds as bytes. */
function isMarkdown(contentType: string, fileName: string): boolean {
  return (
    contentType.toLowerCase().includes('markdown') || /\.(md|markdown)$/i.test(fileName)
  )
}

type BodyState =
  | { status: 'loading' }
  | { status: 'ready'; text: string }
  | { status: 'error' }

/**
 * Right-pane renderer for the original file. Markdown is fetched as text and rendered FORMATTED
 * (headings, lists, tables via GFM) — raw HTML in the source is NOT rendered (react-markdown's safe
 * default), so an uploaded document can't inject markup. PDFs and other formats embed their bytes in an
 * `<iframe>` as before. Both paths go through the typed client, so the single-fetch invariant holds.
 */
function DocumentFileBody({ document }: { document: DocumentSummary }): ReactElement {
  const renderMarkdown = isMarkdown(document.contentType, document.fileName)
  const [body, setBody] = useState<BodyState>({ status: 'loading' })

  useEffect(() => {
    if (!renderMarkdown) {
      return
    }
    let active = true
    setBody({ status: 'loading' })
    void getDocumentFileText(document.id).then((result) => {
      if (!active) {
        return
      }
      setBody(result.ok ? { status: 'ready', text: result.value } : { status: 'error' })
    })
    return () => {
      active = false
    }
  }, [renderMarkdown, document.id])

  if (!renderMarkdown) {
    return (
      <iframe
        className="viewer-frame"
        src={documentFileUrl(document.id)}
        title={`Source file: ${document.fileName}`}
      />
    )
  }

  if (body.status === 'loading') {
    return <p className="hint">Loading file…</p>
  }
  if (body.status === 'error') {
    return (
      <p className="error" role="alert">
        Could not load the file contents.
      </p>
    )
  }
  return (
    <div className="viewer-markdown" aria-label={`Source file: ${document.fileName}`}>
      <ReactMarkdown remarkPlugins={[remarkGfm]}>{body.text}</ReactMarkdown>
    </div>
  )
}

/**
 * Modal viewer for a source document (spec 0006): shows the extracted fields beside the ORIGINAL
 * uploaded file. Markdown renders formatted (spec 0006 amendment); PDF and plain text embed in an
 * `<iframe>`. All reads go through the typed client, so the single-typed-client invariant holds. Closes
 * on the backdrop, the × button, or Escape.
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
            <DocumentFileBody document={state.document} />
          </div>
        )}
      </div>
    </div>
  )
}
