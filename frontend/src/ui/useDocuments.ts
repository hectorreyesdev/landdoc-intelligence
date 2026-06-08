import { useCallback, useRef, useState } from 'react'
import { uploadDocument } from '../api/client'
import type { DocumentResponse } from '../api/types'
import { describeError } from './errorText'

/**
 * One entry in the document grid. A file enters as `uploading` (a grayed placeholder tile) the moment
 * its batch starts, then resolves in place to `ready` (the solid card with fields) or `error`.
 */
export type DocItem =
  | { readonly key: string; readonly status: 'uploading'; readonly fileName: string }
  | { readonly key: string; readonly status: 'ready'; readonly fileName: string; readonly doc: DocumentResponse }
  | { readonly key: string; readonly status: 'error'; readonly fileName: string; readonly message: string }

export interface UploadProgress {
  readonly done: number
  readonly total: number
}

export interface UseDocuments {
  readonly items: readonly DocItem[]
  readonly progress: UploadProgress | null
  readonly hasReady: boolean
  /** Ingest a batch: one `POST /documents` per file, sequentially; tiles update in place as each lands. */
  readonly ingest: (files: File[]) => Promise<void>
}

/**
 * Owns the ingest write path for the UI (spec 0003). The grid renders {@link DocItem}s; the upload
 * control just feeds files in. Keeping it here lets a placeholder tile appear immediately for every
 * file and solidify the instant its upload resolves — independent of where the control lives.
 */
export function useDocuments(): UseDocuments {
  const [items, setItems] = useState<DocItem[]>([])
  const [progress, setProgress] = useState<UploadProgress | null>(null)
  const nextKey = useRef(0)

  const ingest = useCallback(async (batch: File[]): Promise<void> => {
    if (batch.length === 0) {
      return
    }

    // Reserve a stable key per file and show every file as a pending tile up front.
    const keys = batch.map(() => `doc-${nextKey.current++}`)
    setItems((prev) => [
      ...prev,
      ...batch.map((file, index) => ({ key: keys[index], status: 'uploading' as const, fileName: file.name })),
    ])
    setProgress({ done: 0, total: batch.length })

    // Sequential, not parallel: tiles solidify in order and the best-effort field extraction
    // (a chat call per file) doesn't fan out a burst of provider requests.
    for (const [index, file] of batch.entries()) {
      const result = await uploadDocument(file)
      const key = keys[index]
      setItems((prev) =>
        prev.map((item) => {
          if (item.key !== key) {
            return item
          }
          return result.ok
            ? { key, status: 'ready', fileName: file.name, doc: result.value }
            : { key, status: 'error', fileName: file.name, message: describeError(result.error, 'upload') }
        }),
      )
      setProgress({ done: index + 1, total: batch.length })
    }

    setProgress(null)
  }, [])

  const hasReady = items.some((item) => item.status === 'ready')

  return { items, progress, hasReady, ingest }
}
