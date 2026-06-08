import { beforeEach, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { UploadPanel } from './UploadPanel'

beforeEach(() => {
  vi.clearAllMocks()
})

function pdf(name = 'lease.pdf'): File {
  return new File(['%PDF-1.4'], name, { type: 'application/pdf' })
}

it('feeds chosen files to onFiles immediately, with no submit button', async () => {
  const onFiles = vi.fn()
  render(<UploadPanel onFiles={onFiles} progress={null} />)

  expect(screen.queryByRole('button')).not.toBeInTheDocument()
  await userEvent.upload(screen.getByLabelText(/document files/i), pdf('a.pdf'))

  expect(onFiles).toHaveBeenCalledTimes(1)
  expect(onFiles.mock.calls[0][0]).toHaveLength(1)
})

it('lets the file input accept PDFs, text, and markdown, and allows multiple', () => {
  render(<UploadPanel onFiles={() => {}} progress={null} />)
  const input = screen.getByLabelText(/document files/i)
  const accept = input.getAttribute('accept') ?? ''
  expect(accept).toMatch(/\.pdf/)
  expect(accept).toMatch(/\.txt/)
  expect(accept).toMatch(/\.md/)
  expect(input).toHaveAttribute('multiple')
})

it('feeds files dropped onto the page (accepted only) to onFiles', async () => {
  const onFiles = vi.fn()
  render(<UploadPanel onFiles={onFiles} progress={null} />)

  fireEvent.drop(document.body, { dataTransfer: { files: [pdf('dropped.pdf')] } })

  await waitFor(() => expect(onFiles).toHaveBeenCalledTimes(1))
})

it('ignores a drop with no supported files and explains why', async () => {
  const onFiles = vi.fn()
  render(<UploadPanel onFiles={onFiles} progress={null} />)

  const png = new File(['x'], 'photo.png', { type: 'image/png' })
  fireEvent.drop(document.body, { dataTransfer: { files: [png] } })

  expect(await screen.findByText(/drop pdf, text, or markdown/i)).toBeInTheDocument()
  expect(onFiles).not.toHaveBeenCalled()
})

it('shows a progress bar and disables the input while a batch is in flight', () => {
  render(<UploadPanel onFiles={() => {}} progress={{ done: 1, total: 4 }} />)

  const bar = screen.getByRole('progressbar')
  expect(bar).toHaveAttribute('aria-valuenow', '1')
  expect(bar).toHaveAttribute('aria-valuemax', '4')
  expect(screen.getByLabelText(/document files/i)).toBeDisabled()
})
