import type { ReactElement } from 'react'
import type { DocItem } from './useDocuments'

/**
 * One document tile (spec 0003 state 2). Three visual states keyed off the item status:
 *  - `uploading` — a grayed, shimmering placeholder with the file name (it's `aria-busy`);
 *  - `ready`     — the solid card (file name title · status · chunk count · fields), which animates
 *                  in place from the placeholder via the `solidify` keyframes on `.doc-card--ready`;
 *  - `error`     — the file name plus the failure reason.
 */
export function DocumentCard({ item }: { item: DocItem }): ReactElement {
  if (item.status === 'uploading') {
    return (
      <article className="doc-card doc-card--pending" aria-busy="true">
        <h3 className="doc-card-title" title={item.fileName}>
          {item.fileName}
        </h3>
        <p className="doc-meta">Uploading…</p>
        <div className="skeleton" aria-hidden="true">
          <span className="skeleton-line" />
          <span className="skeleton-line skeleton-line--short" />
        </div>
      </article>
    )
  }

  if (item.status === 'error') {
    return (
      <article className="doc-card doc-card--error">
        <h3 className="doc-card-title" title={item.fileName}>
          {item.fileName}
        </h3>
        <p className="doc-meta doc-card-failed">Upload failed</p>
        <p className="doc-empty">{item.message}</p>
      </article>
    )
  }

  const { doc } = item
  return (
    <article className="doc-card doc-card--ready">
      <h3 className="doc-card-title" title={doc.fileName}>
        {doc.fileName}
      </h3>
      <p className="doc-meta">
        {doc.status} · {doc.chunkCount} chunks
      </p>
      {doc.fields.length === 0 ? (
        <p className="doc-empty">No fields extracted.</p>
      ) : (
        <dl className="fields">
          {doc.fields.map((field) => (
            <div className="field" key={field.name}>
              <dt>{field.name}</dt>
              <dd>{field.value}</dd>
            </div>
          ))}
        </dl>
      )}
    </article>
  )
}
