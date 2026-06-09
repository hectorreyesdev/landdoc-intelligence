import { useCallback, useEffect, useState } from 'react'
import { getUsage } from '../../api/client'
import type { UsageRange, UsageReport } from '../../api/types'

export type UsageStatus = 'loading' | 'ready' | 'error'

export interface UsageState {
  readonly status: UsageStatus
  readonly report: UsageReport | null
  readonly range: UsageRange
  setRange: (range: UsageRange) => void
}

/**
 * Loads `GET /usage` for the selected range (spec 0009), re-querying whenever the range changes. The view
 * reads live each time — there is no client-side history. A failed/aborted load lands in `error`/`loading`.
 */
export function useUsage(initial: UsageRange = '24h'): UsageState {
  const [range, setRange] = useState<UsageRange>(initial)
  const [status, setStatus] = useState<UsageStatus>('loading')
  const [report, setReport] = useState<UsageReport | null>(null)

  useEffect(() => {
    let cancelled = false
    setStatus('loading')
    void getUsage(range).then((result) => {
      if (cancelled) return
      if (result.ok) {
        setReport(result.value)
        setStatus('ready')
      } else {
        setStatus('error')
      }
    })
    return () => {
      cancelled = true
    }
  }, [range])

  const setRangeStable = useCallback((next: UsageRange) => setRange(next), [])

  return { status, report, range, setRange: setRangeStable }
}
