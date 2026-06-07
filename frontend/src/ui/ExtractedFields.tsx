import type { ReactElement } from 'react'
import type { DocumentResponse } from '../api/types'

/** The extracted-fields view (spec 0003 state 2): the live ingest response, rendered. */
export function ExtractedFields({ doc }: { doc: DocumentResponse }): ReactElement {
  return (
    <div className="panel-result">
      <h3>Extracted fields</h3>
      <p className="doc-meta">
        <strong>{doc.fileName}</strong> · {doc.status} · {doc.chunkCount} chunks
      </p>
      {doc.fields.length === 0 ? (
        <p>No fields were extracted.</p>
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
    </div>
  )
}
