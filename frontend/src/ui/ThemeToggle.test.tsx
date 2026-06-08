import { beforeEach, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ThemeToggle } from './ThemeToggle'

beforeEach(() => {
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
})

it('defaults to light and applies it to the document root', () => {
  render(<ThemeToggle />)
  expect(document.documentElement.getAttribute('data-theme')).toBe('light')
})

it('toggles to dark, applies it, and persists the choice', async () => {
  render(<ThemeToggle />)

  await userEvent.click(screen.getByRole('button', { name: /switch to dark/i }))

  expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
  expect(localStorage.getItem('landdoc-theme')).toBe('dark')
  // Now offers to switch back to light.
  expect(screen.getByRole('button', { name: /switch to light/i })).toBeInTheDocument()
})

it('respects a stored preference on mount', () => {
  localStorage.setItem('landdoc-theme', 'dark')
  render(<ThemeToggle />)
  expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
})
