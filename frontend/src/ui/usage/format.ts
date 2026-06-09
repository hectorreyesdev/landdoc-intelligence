// Pure formatting helpers for the Ops / Usage view (spec 0009). Kept separate from the component so the
// number/cost shaping is unit-testable without rendering.

const INT = new Intl.NumberFormat('en-US')
const USD = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
})

/** Thousands-separated integer, e.g. 200000 → "200,000". */
export function formatInt(value: number): string {
  return INT.format(value)
}

/** USD with 2–4 fraction digits, e.g. 0.037 → "$0.037". Always an estimate (spec 0009). */
export function formatUsd(value: number): string {
  return USD.format(value)
}
