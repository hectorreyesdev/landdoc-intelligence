import type { ReactElement } from 'react'
import type { DocItem } from './useDocuments'
import { DocumentCard } from './DocumentCard'

/**
 * The ingested-corpus view: every uploaded document (and every in-flight upload) as a compact tile in
 * a wrapping grid, so the whole corpus stays visible without consuming the page vertically (spec 0003
 * — multi-file). Renders nothing until the first file is chosen.
 */
export function DocumentList({ items }: { items: readonly DocItem[] }): ReactElement | null {
  if (items.length === 0) {
    return null
  }
  return (
    <section className="panel doc-panel" aria-labelledby="docs-heading">
      <h2 id="docs-heading">Documents ({items.length})</h2>
      <div className="doc-grid">
        {items.map((item) => (
          <DocumentCard key={item.key} item={item} />
        ))}
      </div>
    </section>
  )
}
