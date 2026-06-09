import { useState, type ReactElement } from 'react'
import { UploadPanel } from './ui/UploadPanel'
import { AskPanel } from './ui/AskPanel'
import { DocumentList } from './ui/DocumentList'
import { DocumentsTable } from './ui/DocumentsTable'
import { DocumentViewer } from './ui/DocumentViewer'
import { Dashboard } from './ui/dashboard/Dashboard'
import { ThemeToggle } from './ui/ThemeToggle'
import { useDocuments } from './ui/useDocuments'
import { useDocumentTable } from './ui/useDocumentTable'

type Tab = 'workspace' | 'dashboard'

/**
 * The vertical slice (spec 0003) + persisted library (spec 0006) + insights (spec 0007). A header tab
 * switches between Workspace (upload · documents · ask) and Dashboard (analytics over the same
 * GET /documents data). Clicking a citation, a table row's "View", or a dashboard item opens the
 * source-document viewer. Both tabs share one document load (`useDocumentTable`), refreshed after upload.
 */
export function App(): ReactElement {
  const { items, progress, hasReady, ingest } = useDocuments()
  const table = useDocumentTable()
  const [viewerId, setViewerId] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('workspace')

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
        <nav className="tabs" aria-label="Views">
          <button
            type="button"
            className={tab === 'workspace' ? 'tab tab--active' : 'tab'}
            aria-pressed={tab === 'workspace'}
            onClick={() => setTab('workspace')}
          >
            Workspace
          </button>
          <button
            type="button"
            className={tab === 'dashboard' ? 'tab tab--active' : 'tab'}
            aria-pressed={tab === 'dashboard'}
            onClick={() => setTab('dashboard')}
          >
            Dashboard
          </button>
        </nav>
        <ThemeToggle />
      </header>

      {tab === 'workspace' ? (
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
      ) : (
        <Dashboard documents={table.documents} status={table.status} onOpenDocument={setViewerId} />
      )}

      {viewerId !== null && (
        <DocumentViewer documentId={viewerId} onClose={() => setViewerId(null)} />
      )}
    </main>
  )
}
