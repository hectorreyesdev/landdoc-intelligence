import { beforeEach, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { UploadPanel } from './UploadPanel'
import * as client from '../api/client'
import type { DocumentResponse } from '../api/types'

vi.mock('../api/client')

beforeEach(() => {
  vi.resetAllMocks()
})

const sampleDoc: DocumentResponse = {
  id: 'doc-1',
  fileName: 'synthetic-lease-01.pdf',
  status: 'ready',
  fields: [
    { name: 'Lessor', value: 'Jane Roe', sourceChunkId: 'c1' },
    { name: 'Royalty', value: '3/16', sourceChunkId: null },
  ],
  chunkCount: 7,
}

function pdf(): File {
  return new File(['%PDF-1.4'], 'synthetic-lease-01.pdf', { type: 'application/pdf' })
}

it('renders extracted fields + chunkCount after a successful upload', async () => {
  vi.mocked(client.uploadDocument).mockResolvedValue({ ok: true, value: sampleDoc })
  render(<UploadPanel onIngested={() => {}} />)

  await userEvent.upload(screen.getByLabelText(/pdf file/i), pdf())
  await userEvent.click(screen.getByRole('button', { name: /upload/i }))

  expect(await screen.findByText('synthetic-lease-01.pdf')).toBeInTheDocument()
  expect(screen.getByText(/7 chunks/i)).toBeInTheDocument()
  expect(screen.getByText('Lessor')).toBeInTheDocument()
  expect(screen.getByText('Jane Roe')).toBeInTheDocument()
  expect(screen.getByText('Royalty')).toBeInTheDocument()
  expect(screen.getByText('3/16')).toBeInTheDocument()
})

it('notifies the app that a document was ingested', async () => {
  vi.mocked(client.uploadDocument).mockResolvedValue({ ok: true, value: sampleDoc })
  const onIngested = vi.fn()
  render(<UploadPanel onIngested={onIngested} />)

  await userEvent.upload(screen.getByLabelText(/pdf file/i), pdf())
  await userEvent.click(screen.getByRole('button', { name: /upload/i }))

  expect(await screen.findByText('synthetic-lease-01.pdf')).toBeInTheDocument()
  expect(onIngested).toHaveBeenCalledWith(sampleDoc)
})

it('shows inline validation when submitting with no file, and never calls the client', async () => {
  render(<UploadPanel onIngested={() => {}} />)

  await userEvent.click(screen.getByRole('button', { name: /upload/i }))

  expect(screen.getByRole('alert')).toHaveTextContent(/choose a pdf/i)
  expect(client.uploadDocument).not.toHaveBeenCalled()
})

it('shows a generic error on a server failure and stays usable', async () => {
  vi.mocked(client.uploadDocument).mockResolvedValue({
    ok: false,
    error: { kind: 'server', status: 500, detail: null },
  })
  render(<UploadPanel onIngested={() => {}} />)

  await userEvent.upload(screen.getByLabelText(/pdf file/i), pdf())
  await userEvent.click(screen.getByRole('button', { name: /upload/i }))

  expect(await screen.findByRole('alert')).toHaveTextContent(/went wrong/i)
  expect(screen.getByRole('button', { name: /upload/i })).toBeEnabled()
})
