import { describe, expect, it } from 'vitest'
import { formatInt, formatUsd } from './format'

describe('formatInt', () => {
  it('thousands-separates integers', () => {
    expect(formatInt(200000)).toBe('200,000')
    expect(formatInt(0)).toBe('0')
  })
})

describe('formatUsd', () => {
  it('formats USD with 2–4 fraction digits', () => {
    expect(formatUsd(0.037)).toBe('$0.037')
    expect(formatUsd(0)).toBe('$0.00')
    expect(formatUsd(12.5)).toBe('$12.50')
  })
})
