import { type ReactElement } from 'react'
import { UploadPanel } from './ui/UploadPanel'
import { AskPanel } from './ui/AskPanel'
import { DocumentList } from './ui/DocumentList'
import { ThemeToggle } from './ui/ThemeToggle'
import { useDocuments } from './ui/useDocuments'

/**
 * The vertical slice (spec 0003): upload → fields, then ask → answer-with-citations. Two independent
 * columns — upload + the ingested-document grid on the left, ask + answer-with-citations on the right
 * — so a long citations list never pushes the document list down the page. The ask path knows only
 * whether *something* has been ingested; it never depends on the upload path.
 */
export function App(): ReactElement {
  const { items, progress, hasReady, ingest } = useDocuments()

  return (
    <main className="app">
      <header className="app-header">
        <h1>LandDoc Intelligence</h1>
        <ThemeToggle />
      </header>
      <div className="columns">
        <div className="column">
          <UploadPanel onFiles={ingest} progress={progress} />
          <DocumentList items={items} />
        </div>
        <div className="column">
          <AskPanel canAsk={hasReady} />
        </div>
      </div>
    </main>
  )
}
