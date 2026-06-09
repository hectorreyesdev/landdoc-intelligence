import { expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
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

it('shows run metadata and links to the methodology', () => {
  render(<EvalQualityCard />)

  expect(screen.getByText(new RegExp(`${summary.caseCount} cases`, 'i'))).toBeInTheDocument()
  expect(screen.getByText(/judge claude-sonnet-4-6/i)).toBeInTheDocument()

  const link = screen.getByRole('link', { name: /methodology/i })
  expect(link.getAttribute('href')).toContain('EVAL-HARNESS.md')
})
