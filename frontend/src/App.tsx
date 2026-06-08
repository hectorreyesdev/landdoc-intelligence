import { useState, type ReactElement } from 'react'
import { UploadPanel } from './ui/UploadPanel'
import { AskPanel } from './ui/AskPanel'
import { DocumentList } from './ui/DocumentList'
import { DocumentsTable } from './ui/DocumentsTable'
import { DocumentViewer } from './ui/DocumentViewer'
import { ThemeToggle } from './ui/ThemeToggle'
import { useDocuments } from './ui/useDocuments'
import { useDocumentTable } from './ui/useDocumentTable'

/**
 * The vertical slice (spec 0003) plus the persisted document library (spec 0006): upload → fields, the
 * full documents table, then ask → answer-with-citations whose citations open the source file. Two
 * independent columns — upload + session grid + persisted table on the left, ask on the right — so a
 * long citations list never pushes the document list down the page. Clicking a citation or a table row's
 * "View" opens the source-document viewer.
 */
export function App(): ReactElement {
  const { items, progress, hasReady, ingest } = useDocuments()
  const table = useDocumentTable()
  const [viewerId, setViewerId] = useState<string | null>(null)

  // After a batch lands, refresh the persisted table so newly ingested documents appear.
  async function handleFiles(files: File[]): Promise<void> {
    await ingest(files)
    await table.reload()
  }

  const canAsk = hasReady || table.documents.length > 0

  return (
    <main className="app">
      <header className="app-header">
        <h1>LandDoc Intelligence</h1>
        <ThemeToggle />
      </header>
      <div className="columns">
        <div className="column">
          <UploadPanel onFiles={handleFiles} progress={progress} />
          <DocumentList items={items} />
          <DocumentsTable documents={table.documents} onOpenDocument={setViewerId} />
        </div>
        <div className="column">
          <AskPanel canAsk={canAsk} onOpenDocument={setViewerId} />
        </div>
      </div>
      {viewerId !== null && (
        <DocumentViewer documentId={viewerId} onClose={() => setViewerId(null)} />
      )}
    </main>
  )
}
