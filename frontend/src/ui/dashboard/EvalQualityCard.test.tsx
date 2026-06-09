import { expect, it } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { EvalQualityCard } from './EvalQualityCard'
import summary from './eval-summary.json'

it('renders the metric means from the committed snapshot', () => {
  render(<EvalQualityCard />)

  expect(screen.getByRole('heading', { name: /answer quality \(eval\)/i })).toBeInTheDocument()

  const kpis = screen.getByLabelText(/eval metric means/i)
  expect(within(kpis).getByText(summary.means.recallAtK.toFixed(2))).toBeInTheDocument()
  expect(within(kpis).getByText(summary.means.groundedness.toFixed(1))).toBeInTheDocument()
  expect(within(kpis).getByText(summary.means.equivalence.toFixed(2))).toBeInTheDocument()
})

it('renders one row per case, with an abstained marker on absent cases', () => {
  render(<EvalQualityCard />)

  // header row + one row per case
  expect(screen.getAllByRole('row')).toHaveLength(summary.cases.length + 1)

  const abstainedCount = summary.cases.filter((c) => c.abstained).length
  expect(abstainedCount).toBeGreaterThan(0)
  expect(screen.getAllByText(/abstained/i)).toHaveLength(abstainedCount)
})

it('expands a case to reveal its question, expected answer, and copy control', () => {
  render(<EvalQualityCard />)

  // Pick a non-abstained case whose id is not contained in another id (so the toggle lookup is unambiguous).
  const target = summary.cases.find(
    (c) => !c.abstained && !summary.cases.some((o) => o.id !== c.id && o.id.includes(c.id)),
  )
  expect(target).toBeDefined()
  const evalCase = target!

  // Collapsed by default: the question text is not in the document.
  expect(screen.queryByText(evalCase.question)).not.toBeInTheDocument()

  const toggle = screen
    .getAllByRole('button')
    .find((b) => b.textContent?.includes(evalCase.id))
  expect(toggle).toBeDefined()
  fireEvent.click(toggle!)

  expect(screen.getByText(evalCase.question)).toBeInTheDocument()
  expect(screen.getByText(evalCase.expectedAnswer)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /copy question/i })).toBeInTheDocument()
})

it('shows run metadata and links to the methodology', () => {
  render(<EvalQualityCard />)

  expect(screen.getByText(new RegExp(`${summary.caseCount} cases`, 'i'))).toBeInTheDocument()
  expect(screen.getByText(/judge claude-sonnet-4-6/i)).toBeInTheDocument()

  const link = screen.getByRole('link', { name: /methodology/i })
  expect(link.getAttribute('href')).toContain('EVAL-HARNESS.md')
})
