import { useState, type ReactElement } from 'react'
import { UploadPanel } from './ui/UploadPanel'
import { AskPanel } from './ui/AskPanel'

/**
 * The vertical slice (spec 0003): upload → fields, then ask → answer-with-citations.
 * The two halves are independent — the ask path knows only whether *something* has been
 * ingested; the upload/fields path never depends on the ask path.
 */
export function App(): ReactElement {
  const [ingestedCount, setIngestedCount] = useState(0)

  return (
    <main className="app">
      <h1>LandDoc Intelligence</h1>
      <UploadPanel onIngested={() => setIngestedCount((count) => count + 1)} />
      <AskPanel canAsk={ingestedCount > 0} />
    </main>
  )
}
