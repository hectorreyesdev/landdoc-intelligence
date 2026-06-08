import { useEffect, useRef, useState, type ChangeEvent, type ReactElement } from 'react'
import type { UploadProgress } from './useDocuments'

interface UploadPanelProps {
  /** Feed a batch of files into ingest (the document grid renders the results). */
  onFiles: (files: File[]) => void | Promise<void>
  /** Non-null while a batch is uploading — drives the progress bar and disables the input. */
  progress: UploadProgress | null
}

// The formats the backend ingests (spec 0005): a text-based PDF, or a UTF-8 text/markdown file. The
// picker's `accept` only filters its own dialog, so dropped files are validated against the extension
// list too (the extension is what the backend dispatches on — DocumentsEndpoints).
const ACCEPTED_EXTENSIONS = ['.pdf', '.txt', '.md', '.markdown']
const ACCEPTED_FILE_TYPES = '.pdf,.txt,.md,.markdown,application/pdf,text/plain,text/markdown'

function isAcceptedFile(file: File): boolean {
  const name = file.name.toLowerCase()
  return ACCEPTED_EXTENSIONS.some((extension) => name.endsWith(extension))
}

/** Spec 0003 state 1: the upload control. No submit button — files ingest the moment they're chosen
 * via the picker or dragged from Finder/Explorer onto the page. While a batch is in flight it shows a
 * progress bar and disables the input. */
export function UploadPanel({ onFiles, progress }: UploadPanelProps): ReactElement {
  const [dragging, setDragging] = useState(false)
  const [dropError, setDropError] = useState<string | null>(null)
  const dragDepth = useRef(0)

  const busy = progress !== null
  const busyRef = useRef(busy)
  busyRef.current = busy

  function handleSelect(event: ChangeEvent<HTMLInputElement>): void {
    const selected = Array.from(event.target.files ?? [])
    // Reset so choosing the same file again still fires onChange.
    event.target.value = ''
    if (selected.length === 0) {
      return
    }
    setDropError(null)
    void onFiles(selected)
  }

  // Drag-and-drop from Finder/Explorer, anywhere on the page. preventDefault on dragover/drop stops the
  // browser from navigating to a dropped file; a depth counter keeps the highlight stable as the
  // pointer crosses child elements. Only accepted extensions ingest; an all-unsupported drop is noted.
  useEffect(() => {
    function onDragOver(event: DragEvent): void {
      event.preventDefault()
    }
    function onDragEnter(event: DragEvent): void {
      event.preventDefault()
      dragDepth.current += 1
      setDragging(true)
    }
    function onDragLeave(): void {
      dragDepth.current -= 1
      if (dragDepth.current <= 0) {
        dragDepth.current = 0
        setDragging(false)
      }
    }
    function onDrop(event: DragEvent): void {
      event.preventDefault()
      dragDepth.current = 0
      setDragging(false)
      if (busyRef.current) {
        setDropError('Wait for the current upload to finish.')
        return
      }
      const dropped = Array.from(event.dataTransfer?.files ?? [])
      if (dropped.length === 0) {
        return
      }
      const accepted = dropped.filter(isAcceptedFile)
      if (accepted.length === 0) {
        setDropError('Drop PDF, text, or Markdown files.')
        return
      }
      setDropError(null)
      void onFiles(accepted)
    }

    window.addEventListener('dragover', onDragOver)
    window.addEventListener('dragenter', onDragEnter)
    window.addEventListener('dragleave', onDragLeave)
    window.addEventListener('drop', onDrop)
    return () => {
      window.removeEventListener('dragover', onDragOver)
      window.removeEventListener('dragenter', onDragEnter)
      window.removeEventListener('dragleave', onDragLeave)
      window.removeEventListener('drop', onDrop)
    }
  }, [onFiles])

  return (
    <section className={dragging ? 'panel dragging' : 'panel'} aria-labelledby="upload-heading">
      <h2 id="upload-heading">Upload documents</h2>
      <input
        type="file"
        accept={ACCEPTED_FILE_TYPES}
        multiple
        aria-label="Document files"
        disabled={busy}
        onChange={handleSelect}
      />
      <p className="hint drop-hint">
        Files upload as soon as you choose them — or drag &amp; drop from Finder/Explorer anywhere on the page.
      </p>
      {progress !== null && (
        <div
          className="progress"
          role="progressbar"
          aria-label="Upload progress"
          aria-valuemin={0}
          aria-valuemax={progress.total}
          aria-valuenow={progress.done}
        >
          <div
            className={progress.done === 0 ? 'progress-bar progress-bar--indeterminate' : 'progress-bar'}
            style={progress.done === 0 ? undefined : { width: `${(progress.done / progress.total) * 100}%` }}
          />
        </div>
      )}
      {dropError !== null && (
        <p className="error" role="alert">
          {dropError}
        </p>
      )}
    </section>
  )
}
