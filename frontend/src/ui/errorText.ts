import type { ApiError } from '../api/client'

/**
 * Maps a typed {@link ApiError} to the user-facing message for a distinct, non-crashing UI
 * state. Each kind reads differently on purpose: 409 ("ingest first") and 501 ("not available")
 * must not look like a generic failure.
 */
export function describeError(error: ApiError, context: 'upload' | 'ask'): string {
  switch (error.kind) {
    case 'validation':
      return error.detail ?? (context === 'ask' ? 'Enter a valid question.' : 'Choose a valid PDF file.')
    case 'empty-store':
      return 'Ingest a document first — there is nothing to search yet.'
    case 'not-implemented':
      return 'Q&A is not available yet.'
    case 'server':
      return 'Something went wrong. Please try again.'
    case 'network':
      return 'Couldn’t reach the server. Please try again.'
  }
}
