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

  // thead row + one section-header row per distinct category + one row per case (none expanded)
  const categoryCount = new Set(summary.cases.map((c) => c.category)).size
  expect(screen.getAllByRole('row')).toHaveLength(summary.cases.length + 1 + categoryCount)

  const abstainedCount = summary.cases.filter((c) => c.abstained).length
  expect(abstainedCount).toBeGreaterThan(0)
  expect(screen.getAllByText(/abstained/i)).toHaveLength(abstainedCount)
})

it('groups cases into category sections in display order', () => {
  render(<EvalQualityCard />)

  // Every distinct category in the snapshot is rendered as a section header.
  const escape = (s: string): string => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const categories = [...new Set(summary.cases.map((c) => c.category))]
  for (const category of categories) {
    expect(
      screen.getByRole('columnheader', { name: new RegExp(escape(category), 'i') }),
    ).toBeInTheDocument()
  }

  // Single-document field lookups come before abstention cases in the rendered order.
  const headers = screen
    .getAllByRole('columnheader')
    .map((h) => h.textContent ?? '')
    .filter((t) => /lookups|retrieval|distractor|abstention/i.test(t))
  const single = headers.findIndex((t) => /single-document/i.test(t))
  const absent = headers.findIndex((t) => /abstention/i.test(t))
  expect(single).toBeGreaterThanOrEqual(0)
  expect(absent).toBeGreaterThan(single)
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
