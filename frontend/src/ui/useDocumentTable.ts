import { useCallback, useEffect, useState } from 'react'
import { listDocuments } from '../api/client'
import type { DocumentSummary } from '../api/types'

export type DocumentTableStatus = 'loading' | 'ready' | 'error'

export interface UseDocumentTable {
  readonly documents: readonly DocumentSummary[]
  readonly status: DocumentTableStatus
  /** Re-fetch the persisted document list (call after an upload so new docs appear). */
  readonly reload: () => Promise<void>
}

/**
 * Loads the persisted document list from the backend (spec 0006) on mount and on demand. Unlike
 * {@link useDocuments} (which owns transient upload tiles for the current session), this reflects the
 * durable corpus — it survives reload and is the source of truth for the documents table.
 */
export function useDocumentTable(): UseDocumentTable {
  const [documents, setDocuments] = useState<readonly DocumentSummary[]>([])
  const [status, setStatus] = useState<DocumentTableStatus>('loading')

  const reload = useCallback(async (): Promise<void> => {
    const result = await listDocuments()
    if (result.ok) {
      setDocuments(result.value)
      setStatus('ready')
    } else {
      setStatus('error')
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  return { documents, status, reload }
}
