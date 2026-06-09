import { useState, type ReactElement } from 'react'
import { UploadPanel } from './ui/UploadPanel'
import { AskPanel } from './ui/AskPanel'
import { DocumentList } from './ui/DocumentList'
import { DocumentsTable } from './ui/DocumentsTable'
import { DocumentViewer } from './ui/DocumentViewer'
import { Dashboard } from './ui/dashboard/Dashboard'
import { EvalQualityCard } from './ui/dashboard/EvalQualityCard'
import { UsageView } from './ui/usage/UsageView'
import { ThemeToggle } from './ui/ThemeToggle'
import { deleteDocument } from './api/client'
import { useDocuments } from './ui/useDocuments'
import { useDocumentTable } from './ui/useDocumentTable'

type Tab = 'workspace' | 'documents' | 'dashboard' | 'eval' | 'usage'

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

  // Delete each selected document from both stores, close the viewer if it showed one of them, then reload.
  async function handleDeleteSelected(ids: readonly string[]): Promise<void> {
    await Promise.all(ids.map((id) => deleteDocument(id)))
    if (viewerId !== null && ids.includes(viewerId)) {
      setViewerId(null)
    }
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
            className={tab === 'documents' ? 'tab tab--active' : 'tab'}
            aria-pressed={tab === 'documents'}
            onClick={() => setTab('documents')}
          >
            Documents
          </button>
          <button
            type="button"
            className={tab === 'dashboard' ? 'tab tab--active' : 'tab'}
            aria-pressed={tab === 'dashboard'}
            onClick={() => setTab('dashboard')}
          >
            Dashboard
          </button>
          <button
            type="button"
            className={tab === 'eval' ? 'tab tab--active' : 'tab'}
            aria-pressed={tab === 'eval'}
            onClick={() => setTab('eval')}
          >
            Eval
          </button>
          <button
            type="button"
            className={tab === 'usage' ? 'tab tab--active' : 'tab'}
            aria-pressed={tab === 'usage'}
            onClick={() => setTab('usage')}
          >
            Ops / Usage
          </button>
        </nav>
        <ThemeToggle />
      </header>

      {tab === 'workspace' && (
        <div className="columns">
          <div className="column">
            <UploadPanel onFiles={handleFiles} progress={progress} />
            <DocumentList items={items} />
          </div>
          <div className="column">
            <AskPanel canAsk={canAsk} onOpenDocument={setViewerId} />
          </div>
        </div>
      )}

      {tab === 'documents' && (
        <DocumentsTable
          documents={table.documents}
          onOpenDocument={setViewerId}
          onDeleteSelected={handleDeleteSelected}
        />
      )}

      {tab === 'dashboard' && (
        <Dashboard documents={table.documents} status={table.status} onOpenDocument={setViewerId} />
      )}

      {tab === 'eval' && <EvalQualityCard />}

      {tab === 'usage' && <UsageView />}

      {viewerId !== null && (
        <DocumentViewer documentId={viewerId} onClose={() => setViewerId(null)} />
      )}
    </main>
  )
}
