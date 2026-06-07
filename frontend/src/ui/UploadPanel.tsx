import { useState, type FormEvent, type ReactElement } from 'react'
import { uploadDocument } from '../api/client'
import type { DocumentResponse } from '../api/types'
import { ExtractedFields } from './ExtractedFields'
import { describeError } from './errorText'

interface UploadPanelProps {
  /** Called after a successful ingest so the app can enable the ask path. */
  onIngested: (doc: DocumentResponse) => void
}

type UploadState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'error'; message: string }

/** Spec 0003 states 1–2: the upload control and the extracted-fields view it produces. */
export function UploadPanel({ onIngested }: UploadPanelProps): ReactElement {
  const [file, setFile] = useState<File | null>(null)
  const [state, setState] = useState<UploadState>({ status: 'idle' })
  const [doc, setDoc] = useState<DocumentResponse | null>(null)

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (file === null) {
      setState({ status: 'error', message: 'Choose a PDF to upload.' })
      return
    }
    setState({ status: 'loading' })
    const result = await uploadDocument(file)
    if (result.ok) {
      setDoc(result.value)
      setState({ status: 'idle' })
      onIngested(result.value)
    } else {
      setState({ status: 'error', message: describeError(result.error, 'upload') })
    }
  }

  const busy = state.status === 'loading'

  return (
    <section className="panel" aria-labelledby="upload-heading">
      <h2 id="upload-heading">Upload a document</h2>
      <form onSubmit={handleSubmit}>
        <input
          type="file"
          accept="application/pdf"
          aria-label="PDF file"
          disabled={busy}
          onChange={(event) => setFile(event.target.files?.[0] ?? null)}
        />
        <button type="submit" disabled={busy}>
          {busy ? 'Uploading…' : 'Upload'}
        </button>
      </form>
      {state.status === 'error' && (
        <p className="error" role="alert">
          {state.message}
        </p>
      )}
      {doc !== null && <ExtractedFields doc={doc} />}
    </section>
  )
}
